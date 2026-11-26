using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletLedger.Domain.Users;

namespace WalletLedger.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Name).HasMaxLength(120).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);

        // O CPF vira só os 11 dígitos no banco; ao ler, já confiamos que é válido.
        builder.Property(u => u.Cpf)
            .HasConversion(cpf => cpf.Digits, digits => Cpf.FromTrustedDigits(digits))
            .HasMaxLength(11)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.Cpf).IsUnique();

        builder.Ignore(u => u.MaskedName);
    }
}
