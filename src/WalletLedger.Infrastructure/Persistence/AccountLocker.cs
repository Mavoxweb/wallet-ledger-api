using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Accounts;

namespace WalletLedger.Infrastructure.Persistence;

/// <summary>
/// Trava contas com <c>SELECT … ORDER BY id FOR UPDATE</c>. A ordem é decidida pelo
/// PostgreSQL, não pelo C#: os dois ordenam UUIDs de jeitos diferentes, e travar em
/// ordens diferentes em duas transações é exatamente o que causa deadlock.
/// </summary>
internal sealed class AccountLocker(WalletDbContext db) : IAccountLocker
{
    public async Task<IReadOnlyDictionary<Guid, Account>> LockAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("LockAsync precisa ser chamado dentro de uma transação.");
        }

        var ids = accountIds.Distinct().ToArray();
        var accounts = await db.Accounts
            .FromSql($"SELECT * FROM accounts WHERE id = ANY({ids}) ORDER BY id FOR UPDATE")
            .ToListAsync(cancellationToken);

        if (accounts.Count != ids.Length)
        {
            throw new InvalidOperationException("Conta não encontrada ao travar.");
        }

        return accounts.ToDictionary(a => a.Id);
    }
}
