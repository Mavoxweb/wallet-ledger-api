using System.Globalization;
using System.Text;
using WalletLedger.Domain.Common;

namespace WalletLedger.Domain.Pix;

/// <summary>
/// Payload do QR code PIX estático no padrão BR Code (EMV® QRCPS), o mesmo que os
/// apps de banco leem. Cada campo é TLV: ID (2 dígitos) + tamanho (2 dígitos) + valor.
/// O último campo (63) é um CRC16 que protege o conteúdo contra erro de leitura.
/// </summary>
public static class BrCode
{
    private const string PixGui = "br.gov.bcb.pix";
    private const int MaxNameLength = 25;
    private const int MaxCityLength = 15;

    public static string BuildStatic(string pixKey, string merchantName, string merchantCity, long? amountCents, string? txid)
    {
        var payload = new StringBuilder()
            .Append(Field("00", "01"))                                            // versão do payload
            .Append(Field("26", Field("00", PixGui) + Field("01", pixKey)))       // conta PIX: GUI + chave
            .Append(Field("52", "0000"))                                          // categoria do comerciante
            .Append(Field("53", "986"));                                          // moeda: real (ISO 4217)

        if (amountCents is not null)
        {
            payload.Append(Field("54", FormatAmount(amountCents.Value)));         // valor fixo, se houver
        }

        payload
            .Append(Field("58", "BR"))
            .Append(Field("59", Clean(merchantName, MaxNameLength)))
            .Append(Field("60", Clean(merchantCity, MaxCityLength)))
            .Append(Field("62", Field("05", string.IsNullOrEmpty(txid) ? "***" : txid)))
            .Append("6304");                                                      // o CRC entra no próprio cálculo

        return payload.Append(Crc16(payload.ToString())).ToString();
    }

    public static (string PixKey, long? AmountCents) Parse(string payload)
    {
        if (string.IsNullOrEmpty(payload) || payload.Length < 8 || !payload[^8..^4].Equals("6304", StringComparison.Ordinal))
        {
            throw Invalid();
        }

        if (!Crc16(payload[..^4]).Equals(payload[^4..], StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid();
        }

        var fields = ReadFields(payload[..^8]);
        if (!fields.TryGetValue("26", out var account))
        {
            throw Invalid();
        }

        var accountFields = ReadFields(account);
        if (!accountFields.TryGetValue("00", out var gui) || !gui.Equals(PixGui, StringComparison.OrdinalIgnoreCase)
            || !accountFields.TryGetValue("01", out var key))
        {
            throw Invalid();
        }

        long? amount = fields.TryGetValue("54", out var rawAmount) ? ParseAmount(rawAmount) : null;
        return (key, amount);
    }

    private static string Field(string id, string value) =>
        string.Create(CultureInfo.InvariantCulture, $"{id}{value.Length:D2}{value}");

    // Centavos → "10.50", sem passar por ponto flutuante.
    private static string FormatAmount(long cents) =>
        string.Create(CultureInfo.InvariantCulture, $"{cents / 100}.{cents % 100:D2}");

    private static long ParseAmount(string value)
    {
        var parts = value.Split('.');
        if (parts.Length > 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var reais))
        {
            throw Invalid();
        }

        var fraction = parts.Length == 2 ? parts[1].PadRight(2, '0') : "00";
        if (fraction.Length != 2 || !long.TryParse(fraction, NumberStyles.None, CultureInfo.InvariantCulture, out var centavos))
        {
            throw Invalid();
        }

        return (reais * 100) + centavos;
    }

    private static Dictionary<string, string> ReadFields(string data)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;
        while (i < data.Length)
        {
            if (i + 4 > data.Length || !int.TryParse(data.AsSpan(i + 2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                || i + 4 + length > data.Length)
            {
                throw Invalid();
            }

            fields[data.Substring(i, 2)] = data.Substring(i + 4, length);
            i += 4 + length;
        }

        return fields;
    }

    // Os campos de texto aceitam só caracteres simples: tira acentos e corta no limite.
    private static string Clean(string text, int maxLength)
    {
        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var plain = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return (plain.Length > maxLength ? plain[..maxLength] : plain).TrimEnd();
    }

    /// <summary>CRC16-CCITT-FALSE: polinômio 0x1021, valor inicial 0xFFFF, em hexadecimal maiúsculo.</summary>
    private static string Crc16(string data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in Encoding.UTF8.GetBytes(data))
        {
            crc ^= (ushort)(b << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
        }

        return crc.ToString("X4", CultureInfo.InvariantCulture);
    }

    private static DomainException Invalid() => new("invalid_pix_key", "QR code PIX inválido.");
}
