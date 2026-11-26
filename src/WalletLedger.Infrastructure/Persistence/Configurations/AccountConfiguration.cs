using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Users;

namespace WalletLedger.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", t =>
            // Defesa em profundidade: mesmo que um bug passe pelo domínio, o banco
            // recusa saldo negativo em conta de cliente.
            t.HasCheckConstraint("ck_accounts_balance_non_negative", "allows_negative OR balance_cents >= 0"));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.SystemCode).HasMaxLength(50);

        builder.HasOne<User>().WithMany().HasForeignKey(a => a.OwnerUserId).OnDelete(DeleteBehavior.Restrict);

        // Uma conta por cliente, e cada conta de sistema existe uma única vez.
        builder.HasIndex(a => a.OwnerUserId).IsUnique().HasFilter("owner_user_id IS NOT NULL");
        builder.HasIndex(a => a.SystemCode).IsUnique().HasFilter("system_code IS NOT NULL");
    }
}
