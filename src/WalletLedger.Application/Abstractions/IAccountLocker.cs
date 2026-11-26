using WalletLedger.Domain.Accounts;

namespace WalletLedger.Application.Abstractions;

public interface IAccountLocker
{
    /// <summary>
    /// Carrega e trava (<c>SELECT … FOR UPDATE</c>) as contas, sempre em ordem crescente
    /// de id, feita pelo PostgreSQL. Precisa estar dentro de uma transação aberta; as
    /// travas duram até o commit ou rollback.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Account>> LockAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);
}
