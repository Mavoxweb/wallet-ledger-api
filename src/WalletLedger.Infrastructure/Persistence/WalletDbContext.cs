using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Audit;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Ledger;
using WalletLedger.Domain.Pix;
using WalletLedger.Domain.Users;

namespace WalletLedger.Infrastructure.Persistence;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options), IWalletDbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<LedgerTransaction> Transactions => Set<LedgerTransaction>();

    public DbSet<Posting> Postings => Set<Posting>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    public DbSet<PixKey> PixKeys => Set<PixKey>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
}
