using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Users;

namespace WalletLedger.Application.Auth;

/// <summary>Emite o par access token + refresh token para um usuário.</summary>
public sealed class TokenIssuer(IWalletDbContext db, ITokenService tokens, TimeProvider clock)
{
    public async Task<TokenResponse> IssueAsync(User user, CancellationToken cancellationToken, RefreshToken? replacing = null)
    {
        var accountId = await db.Accounts
            .Where(a => a.OwnerUserId == user.Id)
            .Select(a => (Guid?)a.Id)
            .SingleOrDefaultAsync(cancellationToken);

        var now = clock.GetUtcNow();
        var refreshToken = tokens.GenerateRefreshToken();
        var stored = RefreshToken.Issue(user.Id, tokens.HashRefreshToken(refreshToken), now, tokens.RefreshTokenLifetime);

        replacing?.Revoke(now, stored.Id);
        db.RefreshTokens.Add(stored);
        await db.SaveChangesAsync(cancellationToken);

        var access = tokens.CreateAccessToken(user, accountId);
        return new TokenResponse(access.Token, refreshToken, access.ExpiresInSeconds);
    }
}

public sealed class LoginHandler(
    IWalletDbContext db,
    IPasswordHasher passwordHasher,
    IValidator<LoginRequest> validator,
    TokenIssuer issuer)
{
    public async Task<TokenResponse> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Mesma resposta para e-mail inexistente e senha errada: não revela quem tem conta.
        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            throw new DomainException("invalid_credentials", "E-mail ou senha inválidos.");
        }

        return await issuer.IssueAsync(user, cancellationToken);
    }
}

public sealed class RefreshHandler(
    IWalletDbContext db,
    ITokenService tokens,
    IValidator<RefreshRequest> validator,
    TokenIssuer issuer,
    TimeProvider clock)
{
    public async Task<TokenResponse> HandleAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var now = clock.GetUtcNow();
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            ?? throw InvalidToken();

        if (stored.IsRevoked)
        {
            // Um token já trocado está sendo usado de novo: pode ter sido roubado.
            // Revoga todos os tokens do usuário para forçar um novo login.
            var active = await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);
            active.ForEach(t => t.Revoke(now));
            await db.SaveChangesAsync(cancellationToken);
            throw InvalidToken();
        }

        if (!stored.IsActive(now))
        {
            throw InvalidToken();
        }

        var user = await db.Users.SingleAsync(u => u.Id == stored.UserId, cancellationToken);
        return await issuer.IssueAsync(user, cancellationToken, replacing: stored);
    }

    private static DomainException InvalidToken() =>
        new("invalid_refresh_token", "Sessão expirada. Faça login novamente.");
}

public sealed class LogoutHandler(IWalletDbContext db, ITokenService tokens, TimeProvider clock)
{
    /// <summary>Revoga o refresh token. Token desconhecido não é erro: o resultado é o mesmo.</summary>
    public async Task HandleAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return;
        }

        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is not null)
        {
            stored.Revoke(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
