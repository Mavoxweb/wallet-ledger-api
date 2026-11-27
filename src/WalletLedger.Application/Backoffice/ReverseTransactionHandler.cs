using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Application.Common;
using WalletLedger.Domain.Audit;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Ledger;

namespace WalletLedger.Application.Backoffice;

public sealed record ReversalResponse(Guid ReversalTransactionId, Guid OriginalTransactionId, long AmountCents, DateTimeOffset CreatedAt);

/// <summary>
/// Estorno feito pelo admin. Tudo numa transação do banco, dentro da idempotência:
/// <list type="number">
/// <item>trava a transação original (dois estornos simultâneos se enfileiram);</item>
/// <item>confere o status <b>depois</b> da trava;</item>
/// <item>trava as contas em ordem crescente de id;</item>
/// <item>aplica os lançamentos opostos (quem devolve precisa ter saldo);</item>
/// <item>marca a original como estornada e grava a auditoria.</item>
/// </list>
/// </summary>
public sealed class ReverseTransactionHandler(
    IWalletDbContext db,
    IIdempotentOperationRunner idempotency,
    ITransactionLocker transactionLocker,
    IAccountLocker accountLocker,
    IValidator<ReasonRequest> validator,
    TimeProvider clock)
{
    private const int MaxDescriptionLength = 140;

    public async Task<IdempotentResult> HandleAsync(
        Guid actorUserId,
        Guid transactionId,
        IdempotencyContext idempotencyContext,
        ReasonRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var reason = request.Reason.Trim();

        var actorRole = await db.Users
            .Where(u => u.Id == actorUserId)
            .Select(u => u.Role)
            .SingleAsync(cancellationToken);

        return await idempotency.RunAsync(actorUserId, idempotencyContext, async ct =>
        {
            var original = await transactionLocker.LockAsync(transactionId, ct)
                ?? throw new DomainException("not_found", "Transação não encontrada.");

            var now = clock.GetUtcNow();
            var description = $"Estorno: {original.Description}";
            if (description.Length > MaxDescriptionLength)
            {
                description = description[..MaxDescriptionLength];
            }

            // Lança reversal_not_allowed ou transaction_already_reversed com a linha já travada.
            var reversal = LedgerTransaction.CreateReversalOf(original, description, now);

            var accounts = await accountLocker.LockAsync(reversal.Postings.Select(p => p.AccountId).ToList(), ct);

            // Débitos primeiro: se quem devolve não tem saldo, falha antes de qualquer crédito.
            foreach (var posting in reversal.Postings.OrderBy(p => p.AmountCents))
            {
                accounts[posting.AccountId].ApplyReversal(posting.AmountCents);
            }

            original.MarkReversed(reversal.Id);
            db.Transactions.Add(reversal);

            var debited = original.Postings.First(p => p.AmountCents < 0).AccountId;
            var credited = original.Postings.First(p => p.AmountCents > 0).AccountId;
            var auditAccount = accounts[debited].OwnerUserId is not null ? debited : credited;
            var details = JsonSerializer.Serialize(
                new
                {
                    amountCents = original.AmountCents,
                    reversalTransactionId = reversal.Id,
                    originalType = original.Type,
                    debitedAccountId = debited,
                    creditedAccountId = credited,
                },
                ApiJson.Options);
            db.AuditEntries.Add(AuditEntry.Create(
                actorUserId,
                actorRole,
                AuditAction.TransactionReversed,
                auditAccount,
                original.Id,
                reason,
                details,
                now));

            await db.SaveChangesAsync(ct);

            return ApiJson.Created(new ReversalResponse(reversal.Id, original.Id, original.AmountCents, now));
        }, cancellationToken);
    }
}
