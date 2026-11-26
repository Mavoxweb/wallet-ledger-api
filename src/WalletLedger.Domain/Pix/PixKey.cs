using System.Net.Mail;
using WalletLedger.Domain.Common;

namespace WalletLedger.Domain.Pix;

public enum PixKeyType
{
    Email,
    Phone,
    Evp,
}

/// <summary>
/// Chave PIX de uma conta. O valor é sempre guardado <b>normalizado</b>, e a busca
/// normaliza do mesmo jeito: "Ana@X.com " e "ana@x.com" são a mesma chave, assim
/// como "(21) 99999-0000" e "+5521999990000".
/// </summary>
public sealed class PixKey
{
    public const int MaxKeysPerAccount = 5;

    private PixKey()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public PixKeyType Type { get; private set; }

    public string Value { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static PixKey Create(Guid accountId, PixKeyType type, string? rawValue, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            AccountId = accountId,
            Type = type,
            // Chave aleatória (EVP): quem gera é o sistema, o valor enviado é ignorado.
            Value = type == PixKeyType.Evp ? Guid.NewGuid().ToString() : Normalize(type, rawValue),
            CreatedAt = now,
        };

    public static string Normalize(PixKeyType type, string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        return type switch
        {
            PixKeyType.Email => NormalizeEmail(value),
            PixKeyType.Phone => NormalizePhone(value),
            PixKeyType.Evp => Guid.TryParse(value, out var evp) ? evp.ToString() : throw Invalid(),
            _ => throw Invalid(),
        };
    }

    private static string NormalizeEmail(string value)
    {
        var email = value.ToLowerInvariant();
        var at = email.IndexOf('@', StringComparison.Ordinal);

        // Exige domínio com ponto: "a@b" não serve como chave PIX.
        if (at <= 0 || !email[(at + 1)..].Contains('.', StringComparison.Ordinal) || !MailAddress.TryCreate(email, out _))
        {
            throw Invalid();
        }

        return email;
    }

    // Celular brasileiro no formato E.164: +55, DDD (2 dígitos) e número (9 dígitos).
    private static string NormalizePhone(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());

        // Sem "+", 11 dígitos são DDD + número e ganham o 55. Com "+", o código do país
        // já veio escrito e precisa ser 55: "+1 415 555 0100" não vira número brasileiro.
        if (digits.Length == 11 && !value.StartsWith('+'))
        {
            digits = "55" + digits;
        }

        if (digits.Length != 13 || !digits.StartsWith("55", StringComparison.Ordinal))
        {
            throw Invalid();
        }

        return "+" + digits;
    }

    private static DomainException Invalid() => new("invalid_pix_key", "Chave PIX inválida.");
}
