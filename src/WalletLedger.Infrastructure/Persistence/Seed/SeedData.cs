using Microsoft.EntityFrameworkCore;
using WalletLedger.Domain.Accounts;

namespace WalletLedger.Infrastructure.Persistence.Seed;

public static class SeedData
{
    /// <summary>Cria as contas de sistema que ainda não existirem. Pode rodar várias vezes.</summary>
    public static async Task EnsureSystemAccountsAsync(WalletDbContext db, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);

        var exists = await db.Accounts.AnyAsync(a => a.SystemCode == Account.SettlementAccountCode, cancellationToken);
        if (!exists)
        {
            db.Accounts.Add(Account.CreateSystem(Account.SettlementAccountCode, clock.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
