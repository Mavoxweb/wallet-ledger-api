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

    [Fact]
    public void Estorno_tem_lancamentos_opostos_e_soma_zero()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var original = LedgerTransaction.Create(TransactionType.Transfer, "PIX", [(from, -5000), (to, 5000)], Now);

        var reversal = LedgerTransaction.CreateReversalOf(original, "Estorno", Now.AddMinutes(5));

        reversal.Type.ShouldBe(TransactionType.Reversal);
        reversal.Status.ShouldBe(TransactionStatus.Completed);
        reversal.Postings.Sum(p => p.AmountCents).ShouldBe(0);
        reversal.Postings.Single(p => p.AccountId == from).AmountCents.ShouldBe(5000);
        reversal.Postings.Single(p => p.AccountId == to).AmountCents.ShouldBe(-5000);
        reversal.AmountCents.ShouldBe(5000);
    }

    [Fact]
    public void MarkReversed_guarda_o_estorno_e_muda_o_status()
    {
        var original = LedgerTransaction.Create(TransactionType.Deposit, "Depósito", [(Guid.NewGuid(), -100), (Guid.NewGuid(), 100)], Now);
        var reversal = LedgerTransaction.CreateReversalOf(original, "Estorno", Now);

        original.MarkReversed(reversal.Id);

        original.Status.ShouldBe(TransactionStatus.Reversed);
        original.ReversedById.ShouldBe(reversal.Id);
    }

    [Fact]
    public void Estornar_um_estorno_lanca_reversal_not_allowed()
    {
        var original = LedgerTransaction.Create(TransactionType.Transfer, "PIX", [(Guid.NewGuid(), -100), (Guid.NewGuid(), 100)], Now);
        var reversal = LedgerTransaction.CreateReversalOf(original, "Estorno", Now);

        Should.Throw<DomainException>(() => LedgerTransaction.CreateReversalOf(reversal, "Estorno", Now))
            .Code.ShouldBe("reversal_not_allowed");
    }

    [Fact]
    public void Estornar_duas_vezes_lanca_transaction_already_reversed()
    {
        var original = LedgerTransaction.Create(TransactionType.Transfer, "PIX", [(Guid.NewGuid(), -100), (Guid.NewGuid(), 100)], Now);
        original.MarkReversed(LedgerTransaction.CreateReversalOf(original, "Estorno", Now).Id);

        Should.Throw<DomainException>(() => LedgerTransaction.CreateReversalOf(original, "Estorno", Now))
            .Code.ShouldBe("transaction_already_reversed");
    }
}
