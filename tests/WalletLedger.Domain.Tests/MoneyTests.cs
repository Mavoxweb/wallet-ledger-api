using Shouldly;
using WalletLedger.Domain.Common;

namespace WalletLedger.Domain.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void Money_ToString_formats_brl()
    {
        Money.FromCents(123456).ToString().ShouldBe("R$ 1.234,56");
    }

    [Fact]
    public void Money_arithmetic_works_in_cents()
    {
        (Money.FromCents(150) + Money.FromCents(50)).Cents.ShouldBe(200);
        (Money.FromCents(150) - Money.FromCents(200)).Cents.ShouldBe(-50);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void Money_IsPositive(long cents, bool expected)
    {
        Money.FromCents(cents).IsPositive.ShouldBe(expected);
    }
}
