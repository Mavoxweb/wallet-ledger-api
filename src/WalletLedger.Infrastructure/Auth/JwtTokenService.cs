using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Users;

namespace WalletLedger.Infrastructure.Auth;

public sealed class JwtTokenService(IOptions<JwtSettings> options, TimeProvider clock) : ITokenService
{
    public const string AccountIdClaim = "account_id";

    private readonly JwtSettings _settings = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_settings.RefreshTokenDays);

    public AccessToken CreateAccessToken(User user, Guid? accountId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(_settings.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [JwtRegisteredClaimNames.Name] = user.Name,
            ["role"] = user.Role.ToString(),
        };
        if (accountId is not null)
        {
            claims[AccountIdClaim] = accountId.Value.ToString();
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            SigningCredentials = new SigningCredentials(SigningKey(_settings), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, (int)lifetime.TotalSeconds);
    }

    public string GenerateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLower(CultureInfo.InvariantCulture);

    public static SymmetricSecurityKey SigningKey(JwtSettings settings) =>
        new(Encoding.UTF8.GetBytes(settings.SigningKey));
}
