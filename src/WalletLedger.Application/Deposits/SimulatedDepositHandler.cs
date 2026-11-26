using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Application.Common;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Ledger;

namespace WalletLedger.Application.Deposits;

public sealed record SimulatedDepositRequest(long AmountCents);

public sealed record SimulatedDepositResponse(Guid TransactionId, long BalanceCents);

public sealed class SimulatedDepositRequestValidator : AbstractValidator<SimulatedDepositRequest>
{
    public SimulatedDepositRequestValidator() =>
        RuleFor(r => r.AmountCents).InclusiveBetween(1, Limits.MaxAmountCents);
}

/// <summary>
/// Depósito de demonstração: credita a conta do cliente a partir da conta de
/// liquidação. No futuro, o hub de pagamentos ocupa este lugar via webhook.
/// </summary>
public sealed class SimulatedDepositHandler(
    IWalletDbContext db,
    IIdempotentOperationRunner idempotency,
    IAccountLocker locker,
    IValidator<SimulatedDepositRequest> validator,
    TimeProvider clock)
{
    public const long MaxPerDepositCents = 100_000;
    public const long MaxPerDayCents = 500_000;

    public async Task<IdempotentResult> HandleAsync(
        Guid userId,
        Guid accountId,
        IdempotencyContext idempotencyContext,
        SimulatedDepositRequest request,
        CancellationToken cancellationToken)
    {
        // Entrada inválida é rejeitada antes de tocar no banco: não vira registro de idempotência.
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        return await idempotency.RunAsync(userId, idempotencyContext, async ct =>
        {
            if (request.AmountCents > MaxPerDepositCents)
            {
                throw new DomainException("deposit_limit", "O depósito simulado aceita até R$ 1.000,00 por vez.");
            }

            var settlementId = await db.Accounts
                .Where(a => a.SystemCode == Account.SettlementAccountCode)
                .Select(a => a.Id)
                .SingleAsync(ct);

            var accounts = await locker.LockAsync([accountId, settlementId], ct);
            var account = accounts[accountId];
            var settlement = accounts[settlementId];

            // Com a conta travada, ninguém deposita nela em paralelo: a soma abaixo é confiável.
            var now = clock.GetUtcNow();
            var (dayStart, dayEnd) = BusinessTime.DayOf(now);
            var depositedToday = await db.Postings
                .Where(p => p.AccountId == accountId && p.AmountCents > 0 && p.CreatedAt >= dayStart && p.CreatedAt < dayEnd)
                .Join(db.Transactions.Where(t => t.Type == TransactionType.Deposit), p => p.TransactionId, t => t.Id, (p, _) => p.AmountCents)
                .SumAsync(ct);

            if (depositedToday + request.AmountCents > MaxPerDayCents)
            {
                throw new DomainException("deposit_limit", "Limite diário de depósito simulado (R$ 5.000,00) atingido.");
            }

            var amount = Money.FromCents(request.AmountCents);
            settlement.Debit(amount);
            account.Credit(amount);

            var transaction = LedgerTransaction.Create(
                TransactionType.Deposit,
                "Depósito simulado",
                [(settlementId, -amount.Cents), (accountId, amount.Cents)],
                now);
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(ct);

            return ApiJson.Created(new SimulatedDepositResponse(transaction.Id, account.BalanceCents));
        }, cancellationToken);
    }
}
