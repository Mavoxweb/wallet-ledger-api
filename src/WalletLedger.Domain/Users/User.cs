namespace WalletLedger.Domain.Users;

public enum Role
{
    Customer,
    Operator,
    Admin,
}

public sealed class User
{
    private User()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Cpf Cpf { get; private set; } = null!;

    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public Role Role { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Primeiro nome completo e iniciais dos demais: "Alex K. M.".</summary>
    public string MaskedName
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(' ', parts.Take(1).Concat(parts.Skip(1).Select(p => $"{p[0]}.")));
        }
    }

    public static User Create(string name, Cpf cpf, string email, string passwordHash, Role role, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            Name = name.Trim(),
            Cpf = cpf,
            Email = NormalizeEmail(email),
            PasswordHash = passwordHash,
            Role = role,
            CreatedAt = now,
        };

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
