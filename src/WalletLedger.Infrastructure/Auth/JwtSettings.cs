using System.ComponentModel.DataAnnotations;

namespace WalletLedger.Infrastructure.Auth;

/// <summary>Configuração do JWT (seção "Jwt"). A chave nunca vai para o repositório em produção.</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = "wallet-ledger";

    [Required]
    public string Audience { get; init; } = "wallet-ledger-clients";

    /// <summary>Chave HMAC-SHA256. Precisa de pelo menos 32 bytes.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 30;
}
