using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Audit;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Ledger;
using WalletLedger.Domain.Pix;
using WalletLedger.Domain.Users;

namespace WalletLedger.Application.Abstractions;

/// <summary>
/// O que os casos de uso precisam do banco. Os handlers usam o EF Core diretamente
/// através desta interface, sem uma camada de repositórios por cima (ver ADR 0001).
/// </summary>
public interface IWalletDbContext
{
    DbSet<User> Users { get; }

    DbSet<Account> Accounts { get; }

    DbSet<LedgerTransaction> Transactions { get; }

    DbSet<Posting> Postings { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<IdempotencyRecord> IdempotencyKeys { get; }

    DbSet<PixKey> PixKeys { get; }

    DbSet<AuditEntry> AuditEntries { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
