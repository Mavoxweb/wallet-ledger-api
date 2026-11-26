using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Ledger;

namespace WalletLedger.Infrastructure.Persistence.Configurations;

internal sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Description).HasMaxLength(140).IsRequired();

        // A lista de lançamentos é privada no domínio; o EF lê e grava pelo campo.
        builder.HasMany(t => t.Postings)
            .WithOne()
            .HasForeignKey(p => p.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.Postings).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PostingConfiguration : IEntityTypeConfiguration<Posting>
{
    public void Configure(EntityTypeBuilder<Posting> builder)
    {
        builder.ToTable("postings");
        builder.HasKey(p => p.Id);

        // Sequencial gerado pelo banco: dá a ordem estável do extrato.
        builder.Property(p => p.Id).UseIdentityAlwaysColumn();

        builder.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Restrict);

        // Índice do extrato: lançamentos de uma conta, do mais novo para o mais antigo.
        builder.HasIndex(p => new { p.AccountId, p.Id }).IsDescending(false, true);
    }
}
