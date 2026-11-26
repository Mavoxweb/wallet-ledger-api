using WalletLedger.Domain.Users;

namespace WalletLedger.Application.Abstractions;

public sealed record AccessToken(string Token, int ExpiresInSeconds);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user, Guid? accountId);

    /// <summary>Token aleatório e opaco (32 bytes em base64url) que vai para o cliente.</summary>
    string GenerateRefreshToken();

    /// <summary>Hash SHA-256 do refresh token, que é o que fica no banco.</summary>
    string HashRefreshToken(string refreshToken);

    TimeSpan RefreshTokenLifetime { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}
