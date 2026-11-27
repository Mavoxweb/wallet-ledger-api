using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Application.Common;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Audit;
using WalletLedger.Domain.Common;

namespace WalletLedger.Application.Backoffice;

/// <summary>Motivo de uma ação da operação. Vai para a trilha de auditoria.</summary>
public sealed record ReasonRequest(string Reason);

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public const int MinLength = 10;
    public const int MaxLength = 500;

    public ReasonRequestValidator() =>
        RuleFor(r => r.Reason)
            .Must(r => r?.Trim().Length is >= MinLength and <= MaxLength)
            .WithMessage($"Descreva o motivo com {MinLength} a {MaxLength} caracteres.");
}

/// <summary>
/// Base do bloqueio e do desbloqueio: trava a conta, muda o status e grava a
/// auditoria na mesma transação do banco. Se qualquer parte falhar, nada fica.
/// </summary>
public abstract class AccountStatusHandler(
    IWalletDbContext db,
    IAccountLocker locker,
    IValidator<ReasonRequest> validator,
    GetAccountDetailHandler detail,
    TimeProvider clock)
{
    protected abstract AuditAction Action { get; }

    protected abstract void Apply(Account account);

    public async Task<BackofficeAccountDetail> HandleAsync(
        Guid actorUserId,
        Guid accountId,
        ReasonRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var actorRole = await db.Users
            .Where(u => u.Id == actorUserId)
            .Select(u => u.Role)
            .SingleAsync(cancellationToken);

        // Só contas de cliente: a conta de liquidação não é bloqueável pela tela.
        var exists = await db.Accounts.AnyAsync(a => a.Id == accountId && a.OwnerUserId != null, cancellationToken);
        if (!exists)
        {
            throw new DomainException("not_found", "Conta não encontrada.");
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var account = (await locker.LockAsync([accountId], cancellationToken))[accountId];
            var previous = account.Status;
            Apply(account);

            var details = JsonSerializer.Serialize(
                new { previousStatus = previous, newStatus = account.Status },
                ApiJson.Options);
            db.AuditEntries.Add(AuditEntry.Create(
                actorUserId,
                actorRole,
                Action,
                accountId,
                transactionId: null,
                request.Reason.Trim(),
                details,
                clock.GetUtcNow()));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await detail.HandleAsync(accountId, cancellationToken);
    }
}

public sealed class BlockAccountHandler(
    IWalletDbContext db,
    IAccountLocker locker,
    IValidator<ReasonRequest> validator,
    GetAccountDetailHandler detail,
    TimeProvider clock) : AccountStatusHandler(db, locker, validator, detail, clock)
{
    protected override AuditAction Action => AuditAction.AccountBlocked;

    protected override void Apply(Account account) => account.Block();
}

public sealed class UnblockAccountHandler(
    IWalletDbContext db,
    IAccountLocker locker,
    IValidator<ReasonRequest> validator,
    GetAccountDetailHandler detail,
    TimeProvider clock) : AccountStatusHandler(db, locker, validator, detail, clock)
{
    protected override AuditAction Action => AuditAction.AccountUnblocked;

    protected override void Apply(Account account) => account.Unblock();
}
