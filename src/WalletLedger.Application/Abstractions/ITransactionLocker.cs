using WalletLedger.Domain.Ledger;

namespace WalletLedger.Application.Abstractions;

public interface ITransactionLocker
{
    /// <summary>
    /// Carrega e trava (<c>SELECT … FOR UPDATE</c>) a transação do razão, com os
    /// lançamentos. Dois estornos simultâneos da mesma transação se enfileiram aqui:
    /// o segundo só lê a linha depois do commit do primeiro. Precisa estar dentro de
    /// uma transação aberta. Devolve <c>null</c> se a transação não existir.
    /// </summary>
    Task<LedgerTransaction?> LockAsync(Guid transactionId, CancellationToken cancellationToken);
}
