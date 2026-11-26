namespace WalletLedger.Domain.Users;

/// <summary>
/// Refresh token de um usuário. O banco guarda só o hash SHA-256: quem rouba o banco
/// não consegue usar os tokens. A cada uso o token é trocado por um novo (rotação),
/// e o antigo aponta para quem o substituiu.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedById { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public static RefreshToken Issue(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };

    public bool IsActive(DateTimeOffset now) => !IsRevoked && now < ExpiresAt;

    public void Revoke(DateTimeOffset now, Guid? replacedById = null)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        ReplacedById = replacedById;
    }
}
