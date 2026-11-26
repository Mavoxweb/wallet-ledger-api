using Microsoft.EntityFrameworkCore;
using Npgsql;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Common;
using WalletLedger.Infrastructure.Persistence;
using WalletLedger.Infrastructure.Persistence.Configurations;

namespace WalletLedger.Infrastructure.Idempotency;

/// <summary>
/// Idempotência atômica (ver ADR 0005). O INSERT da chave é o primeiro comando da
/// mesma transação que grava os lançamentos:
/// <list type="bullet">
/// <item>Duas requisições com a mesma chave ao mesmo tempo: o INSERT da segunda
/// <b>espera</b> no índice único até a primeira terminar.</item>
/// <item>Se a primeira fez commit, a segunda recebe violação de unicidade e devolve
/// a resposta gravada. Se a primeira fez rollback, a segunda segue normalmente.</item>
/// <item>Falha de regra de negócio desfaz tudo, inclusive a chave: o cliente pode
/// tentar de novo com a mesma chave.</item>
/// </list>
/// </summary>
internal sealed class IdempotentOperationRunner(WalletDbContext db, TimeProvider clock) : IIdempotentOperationRunner
{
    public async Task<IdempotentResult> RunAsync(
        Guid userId,
        IdempotencyContext idempotency,
        Func<CancellationToken, Task<IdempotentResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idempotency);
        ArgumentNullException.ThrowIfNull(operation);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var record = IdempotencyRecord.Start(userId, idempotency.Key, idempotency.RequestHash, clock.GetUtcNow());
            db.Add(record);
            await db.SaveChangesAsync(cancellationToken);

            var result = await operation(cancellationToken);

            record.Complete(result.StatusCode, result.JsonBody);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return await ReplayAsync(userId, idempotency, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<IdempotentResult> ReplayAsync(Guid userId, IdempotencyContext idempotency, CancellationToken cancellationToken)
    {
        var stored = await db.Set<IdempotencyRecord>()
            .AsNoTracking()
            .SingleAsync(k => k.UserId == userId && k.Key == idempotency.Key, cancellationToken);

        if (stored.RequestHash != idempotency.RequestHash)
        {
            throw new DomainException("idempotency_key_reused", "Esta chave de idempotência já foi usada com outro pedido.");
        }

        return new IdempotentResult(stored.StatusCode!.Value, stored.ResponseBody!);
    }

    // Só a violação da chave primária de idempotência conta; outras violações de
    // unicidade dentro da operação seguem como erro normal.
    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IdempotencyRecordConfiguration.PrimaryKeyName,
        };
}
