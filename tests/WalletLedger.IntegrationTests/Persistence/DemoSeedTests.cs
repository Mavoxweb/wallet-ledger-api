using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Users;
using WalletLedger.Infrastructure.Persistence;
using WalletLedger.Infrastructure.Persistence.Seed;

namespace WalletLedger.IntegrationTests.Persistence;

[Collection("api")]
public sealed class DemoSeedTests(ApiFactory factory)
{
    /// <summary>Um banco novo no mesmo PostgreSQL, migrado e com as contas de sistema.</summary>
    private async Task<WalletDbContext> FreshDatabaseAsync()
    {
        var name = $"seed_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(factory.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { Database = name }.ConnectionString;
        var db = new WalletDbContext(new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention()
            .Options);
        await db.Database.MigrateAsync();
        await SeedData.EnsureSystemAccountsAsync(db, TimeProvider.System);
        return db;
    }

    private IPasswordHasher Hasher() => factory.Services.GetRequiredService<IPasswordHasher>();

    [Fact]
    public async Task Banco_antigo_com_parte_do_seed_recebe_quem_falta_sem_duplicar()
    {
        await using var db = await FreshDatabaseAsync();
        // Como um banco criado antes de o seed ganhar clientes novos: só a Ana existe.
        var now = TimeProvider.System.GetUtcNow();
        var ana = User.Create("Ana Beatriz Lima", Cpf.FromTrustedDigits("52998224725"), "ana@walletledger.dev", "x", Role.Customer, now);
        db.Users.Add(ana);
        db.Accounts.Add(Account.OpenForCustomer(ana.Id, now));
        await db.SaveChangesAsync();

        await DemoSeed.EnsureDemoDataAsync(db, Hasher(), TimeProvider.System);

        (await db.Users.CountAsync()).ShouldBe(10);
        (await db.Users.CountAsync(u => u.Email == "ana@walletledger.dev")).ShouldBe(1);
        (await db.Users.AnyAsync(u => u.Email == "henrique@walletledger.dev")).ShouldBeTrue();
        (await db.Postings.SumAsync(p => p.AmountCents)).ShouldBe(0);
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Rodar_o_seed_duas_vezes_nao_duplica_nada()
    {
        await using var db = await FreshDatabaseAsync();

        await DemoSeed.EnsureDemoDataAsync(db, Hasher(), TimeProvider.System);
        await DemoSeed.EnsureDemoDataAsync(db, Hasher(), TimeProvider.System);

        (await db.Users.CountAsync()).ShouldBe(10);
        (await db.Accounts.CountAsync(a => a.OwnerUserId != null)).ShouldBe(8);
        (await db.Transactions.CountAsync()).ShouldBe(8);
        await db.Database.EnsureDeletedAsync();
    }
}
