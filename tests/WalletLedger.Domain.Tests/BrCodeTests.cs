using Shouldly;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Pix;

namespace WalletLedger.Domain.Tests;

public sealed class BrCodeTests
{
    // Exemplo de QR estático do Manual do BR Code (Banco Central do Brasil).
    private const string ReferencePayload =
        "00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-4266554400005204000053039865802BR" +
        "5913Fulano de Tal6008BRASILIA62070503***63041D3D";

    [Fact]
    public void BuildStatic_matches_reference_payload()
    {
        var payload = BrCode.BuildStatic("123e4567-e12b-12d1-a456-426655440000", "Fulano de Tal", "BRASILIA", amountCents: null, txid: null);

        payload.ShouldBe(ReferencePayload);
    }

    [Fact]
    public void BuildStatic_with_amount_writes_field_54_with_two_decimals()
    {
        var payload = BrCode.BuildStatic("ana@walletledger.dev", "Ana Beatriz Lima", "SAO PAULO", amountCents: 1050, txid: null);

        payload.ShouldContain("540510.50");
        BrCode.Parse(payload).AmountCents.ShouldBe(1050);
    }

    [Fact]
    public void BuildStatic_removes_accents_and_truncates_name_and_city()
    {
        var payload = BrCode.BuildStatic("chave", "João Conceição de Araújo Magalhães", "São José dos Campos", null, null);

        // Nome: até 25 caracteres ("Joao Conceicao de Araujo " → espaço final removido = 24).
        payload.ShouldContain("5924Joao Conceicao de Araujo6015");
        // Cidade: até 15 caracteres.
        payload.ShouldContain("6015Sao Jose dos Ca62");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    [InlineData(123_456L)]
    public void Parse_round_trips_BuildStatic(long? amountCents)
    {
        var payload = BrCode.BuildStatic("+5521999990000", "Bruno Dias", "RIO DE JANEIRO", amountCents, null);

        var parsed = BrCode.Parse(payload);

        parsed.PixKey.ShouldBe("+5521999990000");
        parsed.AmountCents.ShouldBe(amountCents);
    }

    [Fact]
    public void Parse_reads_reference_payload()
    {
        var parsed = BrCode.Parse(ReferencePayload);

        parsed.PixKey.ShouldBe("123e4567-e12b-12d1-a456-426655440000");
        parsed.AmountCents.ShouldBeNull();
    }

    [Fact]
    public void Parse_rejects_wrong_crc()
    {
        var tampered = ReferencePayload[..^4] + "0000";

        Should.Throw<DomainException>(() => BrCode.Parse(tampered)).Code.ShouldBe("invalid_pix_key");
    }

    [Theory]
    [InlineData("")]
    [InlineData("isto não é um br code")]
    [InlineData("000201")]
    public void Parse_rejects_garbage(string payload)
    {
        Should.Throw<DomainException>(() => BrCode.Parse(payload)).Code.ShouldBe("invalid_pix_key");
    }
}
