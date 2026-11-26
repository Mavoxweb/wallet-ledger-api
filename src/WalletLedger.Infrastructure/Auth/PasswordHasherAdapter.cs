using Microsoft.AspNetCore.Identity;
using WalletLedger.Domain.Users;

namespace WalletLedger.Infrastructure.Auth;

/// <summary>
/// Usa só o hasher do ASP.NET Core Identity (PBKDF2 com salt e muitas iterações),
/// sem trazer o Identity inteiro, que tem tabelas e conceitos que este projeto não usa.
/// </summary>
internal sealed class PasswordHasherAdapter : Application.Abstractions.IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string passwordHash, string password) =>
        _hasher.VerifyHashedPassword(null!, passwordHash, password) != PasswordVerificationResult.Failed;
}
