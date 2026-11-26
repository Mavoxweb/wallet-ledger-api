using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Pix;

namespace WalletLedger.Infrastructure.Persistence.Configurations;

internal sealed class PixKeyConfiguration : IEntityTypeConfiguration<PixKey>
{
    public void Configure(EntityTypeBuilder<PixKey> builder)
    {
        builder.ToTable("pix_keys");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.Type).HasConversion<string>().HasMaxLength(10);
        builder.Property(k => k.Value).HasMaxLength(254).IsRequired();

        // Uma chave pertence a uma única conta no sistema inteiro.
        builder.HasIndex(k => k.Value).IsUnique();
        builder.HasIndex(k => k.AccountId);

        builder.HasOne<Account>().WithMany().HasForeignKey(k => k.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
