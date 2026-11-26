using Shouldly;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Pix;

namespace WalletLedger.Domain.Tests;

public sealed class PixKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Alex@X.com ", "alex@x.com")]
    [InlineData("alex@x.com", "alex@x.com")]
    [InlineData("  ANA.LIMA@Exemplo.COM.BR", "ana.lima@exemplo.com.br")]
    public void Email_variants_normalize_to_same_value(string raw, string expected)
    {
        PixKey.Normalize(PixKeyType.Email, raw).ShouldBe(expected);
    }

    [Theory]
    [InlineData("(21) 99999-0000")]
    [InlineData("21999990000")]
    [InlineData("+5521999990000")]
    [InlineData("+55 (21) 9 9999-0000")]
    [InlineData("5521999990000")]
    public void Phone_variants_normalize_to_same_value(string raw)
    {
        PixKey.Normalize(PixKeyType.Phone, raw).ShouldBe("+5521999990000");
    }

    [Theory]
    [InlineData(PixKeyType.Email, "sem-arroba")]
    [InlineData(PixKeyType.Email, "a@b")]
    [InlineData(PixKeyType.Email, "")]
    [InlineData(PixKeyType.Email, "Ana <ana@x.com>")]
    [InlineData(PixKeyType.Email, "ana(comentario)@x.com")]
    [InlineData(PixKeyType.Phone, "9999-0000")]
    [InlineData(PixKeyType.Phone, "+1 415 555 0100")]
    [InlineData(PixKeyType.Phone, "")]
    public void Invalid_values_throw_invalid_pix_key(PixKeyType type, string raw)
    {
        Should.Throw<DomainException>(() => PixKey.Normalize(type, raw)).Code.ShouldBe("invalid_pix_key");
    }

    [Fact]
    public void Email_key_longer_than_77_chars_is_rejected()
    {
        var at77 = new string('a', 77 - "@x.com".Length) + "@x.com";
        var at78 = new string('a', 78 - "@x.com".Length) + "@x.com";

        PixKey.Normalize(PixKeyType.Email, at77).ShouldBe(at77);
        Should.Throw<DomainException>(() => PixKey.Normalize(PixKeyType.Email, at78)).Code.ShouldBe("invalid_pix_key");
    }

    [Fact]
    public void Evp_key_is_generated_and_ignores_given_value()
    {
        var key = PixKey.Create(Guid.NewGuid(), PixKeyType.Evp, "qualquer-coisa", Now);

        Guid.TryParse(key.Value, out _).ShouldBeTrue();
        key.Value.ShouldBe(key.Value.ToLowerInvariant());
    }

    [Fact]
    public void Evp_lookup_value_is_normalized_to_lowercase()
    {
        PixKey.Normalize(PixKeyType.Evp, " 0B7A3C1E-1D2F-4A5B-8C9D-0E1F2A3B4C5D ").ShouldBe("0b7a3c1e-1d2f-4a5b-8c9d-0e1f2a3b4c5d");
    }
}
