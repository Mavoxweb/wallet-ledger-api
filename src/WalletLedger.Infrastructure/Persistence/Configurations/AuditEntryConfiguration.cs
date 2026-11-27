using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Audit;
using WalletLedger.Domain.Ledger;
using WalletLedger.Domain.Users;

namespace WalletLedger.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActorRole).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(40);
        builder.Property(a => a.Reason).HasMaxLength(500).IsRequired();

        // Aqui jsonb serve: o detalhe é para consulta, não precisa voltar byte a byte.
        builder.Property(a => a.Details).HasColumnType("jsonb").IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(a => a.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(a => a.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LedgerTransaction>().WithMany().HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.Restrict);

        // A trilha é lida por conta e da mais nova para a mais antiga.
        builder.HasIndex(a => new { a.AccountId, a.CreatedAt });
        builder.HasIndex(a => a.CreatedAt);
    }
}
