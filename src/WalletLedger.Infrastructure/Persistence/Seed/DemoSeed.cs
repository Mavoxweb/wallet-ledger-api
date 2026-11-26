using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Ledger;
using WalletLedger.Domain.Pix;
using WalletLedger.Domain.Users;

namespace WalletLedger.Infrastructure.Persistence.Seed;

/// <summary>
/// Usuários de demonstração, para quem clona o projeto ou abre a demo conseguir
/// testar na hora. Só roda com <c>Seed:Demo=true</c>. Pode rodar várias vezes.
/// </summary>
public static class DemoSeed
{
    public const string Password = "Demo@1234";
    public const long InitialBalanceCents = 250_000;

    // CPFs válidos gerados para demonstração (não pertencem a ninguém real conhecido).
    private static readonly (string Name, string Email, string Cpf, Role Role)[] People =
    [
        ("Operador Demo", "operador@walletledger.dev", "11144477735", Role.Operator),
        ("Admin Demo", "admin@walletledger.dev", "39053344705", Role.Admin),
        ("Ana Beatriz Lima", "ana@walletledger.dev", "52998224725", Role.Customer),
        ("Bruno Carvalho Dias", "bruno@walletledger.dev", "15350946056", Role.Customer),
    ];

    public static async Task EnsureDemoDataAsync(
        WalletDbContext db,
        IPasswordHasher passwordHasher,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(clock);

        if (await db.Users.AnyAsync(u => u.Email == "ana@walletledger.dev", cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var settlement = await db.Accounts.SingleAsync(a => a.SystemCode == Account.SettlementAccountCode, cancellationToken);
        var passwordHash = passwordHasher.Hash(Password);

        foreach (var (name, email, cpfDigits, role) in People)
        {
            var user = User.Create(name, Cpf.FromTrustedDigits(cpfDigits), email, passwordHash, role, now);
            db.Users.Add(user);

            if (role != Role.Customer)
            {
                continue;
            }

            var account = Account.OpenForCustomer(user.Id, now);
            db.Accounts.Add(account);
            db.PixKeys.Add(PixKey.Create(account.Id, PixKeyType.Email, email, now));

            // Saldo inicial entra como um depósito de verdade no razão, nunca editando o saldo.
            var amount = Money.FromCents(InitialBalanceCents);
            settlement.Debit(amount);
            account.Credit(amount);
            db.Transactions.Add(LedgerTransaction.Create(
                TransactionType.Deposit,
                "Saldo inicial de demonstração",
                [(settlement.Id, -amount.Cents), (account.Id, amount.Cents)],
                now));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
