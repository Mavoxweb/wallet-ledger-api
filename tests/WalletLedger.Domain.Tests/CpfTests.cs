using Shouldly;
using WalletLedger.Domain.Users;

namespace WalletLedger.Domain.Tests;

public sealed class CpfTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    public void Cpf_TryParse_accepts_valid_with_and_without_mask(string input)
    {
        Cpf.TryParse(input, out var cpf).ShouldBeTrue();
        cpf!.Digits.ShouldBe("52998224725");
    }

    [Theory]
    [InlineData("529.982.247-26")]
    [InlineData("111.111.111-11")]
    [InlineData("1234")]
    [InlineData("")]
    public void Cpf_TryParse_rejects_wrong_check_digits_and_repeated_digits(string input)
    {
        Cpf.TryParse(input, out var cpf).ShouldBeFalse();
        cpf.ShouldBeNull();
    }

    [Fact]
    public void Cpf_Masked_hides_first_and_last_digits()
    {
        Cpf.TryParse("52998224725", out var cpf).ShouldBeTrue();
        cpf!.Masked.ShouldBe("***.982.247-**");
    }
}
