using Shouldly;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Common;

namespace WalletLedger.Domain.Tests;

public sealed class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void Account_Debit_below_zero_throws_insufficient_funds()
    {
        var account = Account.OpenForCustomer(Guid.NewGuid(), Now);
        account.Credit(Money.FromCents(100));

        var ex = Should.Throw<DomainException>(() => account.Debit(Money.FromCents(101)));

        ex.Code.ShouldBe("insufficient_funds");
        account.BalanceCents.ShouldBe(100);
    }

    [Fact]
    public void Account_Debit_up_to_balance_is_allowed()
    {
        var account = Account.OpenForCustomer(Guid.NewGuid(), Now);
        account.Credit(Money.FromCents(100));

        account.Debit(Money.FromCents(100));

        account.BalanceCents.ShouldBe(0);
    }

    [Fact]
    public void System_account_Debit_below_zero_is_allowed()
    {
        var settlement = Account.CreateSystem(Account.SettlementAccountCode, Now);

        settlement.Debit(Money.FromCents(500));

        settlement.BalanceCents.ShouldBe(-500);
        settlement.AllowsNegative.ShouldBeTrue();
    }

    [Fact]
    public void Blocked_account_Credit_and_Debit_throw_account_not_active()
    {
        var account = Account.OpenForCustomer(Guid.NewGuid(), Now);
        account.Credit(Money.FromCents(100));
        account.Block();

        account.Status.ShouldBe(AccountStatus.Blocked);
        Should.Throw<DomainException>(() => account.Credit(Money.FromCents(1))).Code.ShouldBe("account_not_active");
        Should.Throw<DomainException>(() => account.Debit(Money.FromCents(1))).Code.ShouldBe("account_not_active");
    }

    [Fact]
    public void Block_de_conta_bloqueada_lanca_account_already_blocked()
    {
        var account = Account.OpenForCustomer(Guid.NewGuid(), Now);
        account.Block();

        Should.Throw<DomainException>(account.Block).Code.ShouldBe("account_already_blocked");
        account.Status.ShouldBe(AccountStatus.Blocked);
    }

    [Fact]
    public void Unblock_de_conta_ativa_lanca_account_not_blocked()
    {
        var account = Account.OpenForCustomer(Guid.NewGuid(), Now);

        Should.Throw<DomainException>(account.Unblock).Code.ShouldBe("account_not_blocked");
        account.Status.ShouldBe(AccountStatus.Active);
    }

    [Fact]
    public void Unblock_devolve_a_conta_ao_uso()
    {
        var account = Account.OpenForCustomer(Guid.NewGuid(), Now);
        account.Block();

        account.Unblock();

        account.Status.ShouldBe(AccountStatus.Active);
        account.Credit(Money.FromCents(1));
        account.BalanceCents.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Credit_and_Debit_reject_non_positive_amounts(long cents)
    {
        var account = Account.OpenForCustomer(Guid.NewGuid(), Now);

        Should.Throw<DomainException>(() => account.Credit(Money.FromCents(cents))).Code.ShouldBe("invalid_amount");
        Should.Throw<DomainException>(() => account.Debit(Money.FromCents(cents))).Code.ShouldBe("invalid_amount");
    }

    [Fact]
    public void ApplyReversal_vale_para_conta_bloqueada()
    {
        var account = Account.OpenForCustomer(Guid.NewGuid(), Now);
        account.Credit(Money.FromCents(500));
        account.Block();

        account.ApplyReversal(-500);

        account.BalanceCents.ShouldBe(0);
    }

    [Fact]
    public void ApplyReversal_nao_deixa_conta_de_cliente_negativa()
    {
        var account = Account.OpenForCustomer(Guid.NewGuid(), Now);
        account.Credit(Money.FromCents(100));

        Should.Throw<DomainException>(() => account.ApplyReversal(-101)).Code.ShouldBe("insufficient_funds");
        account.BalanceCents.ShouldBe(100);
    }
}
