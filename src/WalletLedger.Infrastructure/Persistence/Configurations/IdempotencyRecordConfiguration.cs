using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Users;

namespace WalletLedger.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public const string PrimaryKeyName = "pk_idempotency_keys";

    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys");

        // A chave é única por usuário: dois clientes podem usar o mesmo texto sem colidir.
        builder.HasKey(k => new { k.UserId, k.Key }).HasName(PrimaryKeyName);

        builder.Property(k => k.Key).HasMaxLength(100);
        builder.Property(k => k.RequestHash).HasMaxLength(64).IsFixedLength();
        // "text", não "jsonb": o jsonb reordena as chaves, e a repetição precisa devolver os mesmos bytes.
        builder.Property(k => k.ResponseBody).HasColumnType("text");
        builder.HasIndex(k => k.CreatedAt);

        builder.HasOne<User>().WithMany().HasForeignKey(k => k.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
