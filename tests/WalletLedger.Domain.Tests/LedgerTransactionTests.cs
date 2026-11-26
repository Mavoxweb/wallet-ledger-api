using Shouldly;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Ledger;

namespace WalletLedger.Domain.Tests;

public sealed class LedgerTransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void LedgerTransaction_Create_with_balanced_postings_succeeds()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();

        var tx = LedgerTransaction.Create(TransactionType.Transfer, "PIX", [(from, -100), (to, 100)], Now);

        tx.Postings.Count.ShouldBe(2);
        tx.Postings.Sum(p => p.AmountCents).ShouldBe(0);
        tx.Postings.ShouldAllBe(p => p.TransactionId == tx.Id);
        tx.Status.ShouldBe(TransactionStatus.Completed);
    }

    [Fact]
    public void LedgerTransaction_Create_rejects_unbalanced_postings()
    {
        var ex = Should.Throw<DomainException>(() =>
            LedgerTransaction.Create(TransactionType.Transfer, "PIX", [(Guid.NewGuid(), 100), (Guid.NewGuid(), -99)], Now));

        ex.Code.ShouldBe("unbalanced_transaction");
    }

    [Fact]
    public void LedgerTransaction_Create_rejects_single_posting()
    {
        var ex = Should.Throw<DomainException>(() =>
            LedgerTransaction.Create(TransactionType.Deposit, "Depósito", [(Guid.NewGuid(), 0)], Now));

        ex.Code.ShouldBe("unbalanced_transaction");
    }

    [Fact]
    public void LedgerTransaction_Create_rejects_zero_amount_posting()
    {
        var ex = Should.Throw<DomainException>(() =>
            LedgerTransaction.Create(TransactionType.Transfer, "PIX", [(Guid.NewGuid(), 0), (Guid.NewGuid(), 0)], Now));

        ex.Code.ShouldBe("invalid_amount");
    }
}
