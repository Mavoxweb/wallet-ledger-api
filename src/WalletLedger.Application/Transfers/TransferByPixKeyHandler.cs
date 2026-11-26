using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Application.Common;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Ledger;
using WalletLedger.Domain.Pix;

namespace WalletLedger.Application.Transfers;

public sealed record PixTransferRequest(PixKeyType KeyType, string KeyValue, long AmountCents, string? Description);

public sealed record PixTransferResponse(
    Guid TransactionId,
    long AmountCents,
    string RecipientMaskedName,
    long BalanceCents,
    DateTimeOffset CreatedAt);

public sealed class PixTransferRequestValidator : AbstractValidator<PixTransferRequest>
{
    public PixTransferRequestValidator()
    {
        RuleFor(r => r.KeyValue).NotEmpty().MaximumLength(254);
        RuleFor(r => r.AmountCents).InclusiveBetween(1, Limits.MaxAmountCents);
        RuleFor(r => r.Description).MaximumLength(140);
    }
}

/// <summary>
/// Transferência por chave PIX. A segurança contra saldo negativo e contra deadlock
/// vem de três peças juntas (ver ADR 0004):
/// <list type="number">
/// <item>tudo roda numa transação de banco (aberta pelo executor idempotente);</item>
/// <item>as duas contas são travadas com <c>FOR UPDATE</c> antes de ler o saldo;</item>
/// <item>a trava é sempre feita em ordem crescente de id, decidida pelo PostgreSQL.</item>
/// </list>
/// </summary>
public sealed class TransferByPixKeyHandler(
    IWalletDbContext db,
    IIdempotentOperationRunner idempotency,
    IAccountLocker locker,
    IValidator<PixTransferRequest> validator,
    TimeProvider clock)
{
    public async Task<IdempotentResult> HandleAsync(
        Guid userId,
        Guid payerAccountId,
        IdempotencyContext idempotencyContext,
        PixTransferRequest request,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var normalizedKey = PixKey.Normalize(request.KeyType, request.KeyValue);

        return await idempotency.RunAsync(userId, idempotencyContext, async ct =>
        {
            var payeeAccountId = await db.PixKeys
                .Where(k => k.Type == request.KeyType && k.Value == normalizedKey)
                .Select(k => (Guid?)k.AccountId)
                .SingleOrDefaultAsync(ct)
                ?? throw new DomainException("not_found", "Chave PIX não encontrada.");

            if (payeeAccountId == payerAccountId)
            {
                throw new DomainException("self_transfer", "Não é possível transferir para a própria conta.");
            }

            // Daqui até o commit, nenhuma outra transação mexe nessas duas contas.
            var accounts = await locker.LockAsync([payerAccountId, payeeAccountId], ct);
            var payer = accounts[payerAccountId];
            var payee = accounts[payeeAccountId];

            // Checa as duas antes de mexer em qualquer saldo.
            payer.EnsureActive();
            payee.EnsureActive();

            var amount = Money.FromCents(request.AmountCents);
            payer.Debit(amount);   // lança insufficient_funds se não houver saldo
            payee.Credit(amount);

            var now = clock.GetUtcNow();
            var transaction = LedgerTransaction.Create(
                TransactionType.Transfer,
                string.IsNullOrWhiteSpace(request.Description) ? "PIX" : request.Description.Trim(),
                [(payerAccountId, -amount.Cents), (payeeAccountId, amount.Cents)],
                now);
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(ct);

            var recipient = await db.Users.AsNoTracking().SingleAsync(u => u.Id == payee.OwnerUserId, ct);

            return ApiJson.Created(new PixTransferResponse(
                transaction.Id, amount.Cents, recipient.MaskedName, payer.BalanceCents, now));
        }, cancellationToken);
    }
}
