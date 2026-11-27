using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Ledger;

namespace WalletLedger.Infrastructure.Persistence;

internal sealed class TransactionLocker(WalletDbContext db) : ITransactionLocker
{
    public async Task<LedgerTransaction?> LockAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("LockAsync precisa ser chamado dentro de uma transação.");
        }

        var transaction = await db.Transactions
            .FromSql($"SELECT * FROM transactions WHERE id = {transactionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (transaction is not null)
        {
            // Os lançamentos entram no mesmo contexto e o EF os liga à transação rastreada.
            await db.Postings.Where(p => p.TransactionId == transactionId).LoadAsync(cancellationToken);
        }

        return transaction;
    }
}
