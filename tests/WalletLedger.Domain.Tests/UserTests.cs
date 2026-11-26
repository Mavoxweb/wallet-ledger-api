using Shouldly;
using WalletLedger.Domain.Users;

namespace WalletLedger.Domain.Tests;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.FromHours(-3));

    private static User NewUser(string name, string email)
    {
        Cpf.TryParse("52998224725", out var cpf).ShouldBeTrue();
        return User.Create(name, cpf!, email, "hash", Role.Customer, Now);
    }

    [Theory]
    [InlineData("Alex Keen Marques", "Alex K. M.")]
    [InlineData("  Ana   Souza ", "Ana S.")]
    [InlineData("Bruno", "Bruno")]
    public void User_MaskedName(string name, string expected)
    {
        NewUser(name, "a@b.com").MaskedName.ShouldBe(expected);
    }

    [Fact]
    public void User_email_is_trimmed_and_lowercased()
    {
        NewUser("Ana", "  Ana@Example.COM ").Email.ShouldBe("ana@example.com");
    }
}
