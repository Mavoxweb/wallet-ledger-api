using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Ledger;
using WalletLedger.Domain.Users;
using WalletLedger.Infrastructure.Persistence;

namespace WalletLedger.IntegrationTests.Persistence;

[Collection("api")]
public sealed class PersistenceTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private WalletDbContext NewContext()
    {
        // Força a subida da API (migrations + seed) antes de abrir o contexto.
        _ = factory.Server;
        return factory.Services.CreateScope().ServiceProvider.GetRequiredService<WalletDbContext>();
    }

    [Fact]
    public async Task Settlement_account_exists_after_startup()
    {
        await using var db = NewContext();

        var count = await db.Accounts.CountAsync(a => a.SystemCode == Account.SettlementAccountCode);

        count.ShouldBe(1);
    }

    [Fact]
    public async Task Transaction_with_postings_round_trips()
    {
        // Lançamentos têm chave estrangeira para contas: precisam de contas reais.
        var from = Account.CreateSystem($"teste-origem-{Guid.NewGuid():N}", Now);
        var to = Account.CreateSystem($"teste-destino-{Guid.NewGuid():N}", Now);
        var tx = LedgerTransaction.Create(TransactionType.Transfer, "PIX de teste", [(from.Id, -250), (to.Id, 250)], Now);

        await using (var db = NewContext())
        {
            db.Accounts.AddRange(from, to);
            db.Transactions.Add(tx);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var loaded = await db.Transactions.Include(t => t.Postings).SingleAsync(t => t.Id == tx.Id);

            loaded.Description.ShouldBe("PIX de teste");
            loaded.Postings.Count.ShouldBe(2);
            loaded.Postings.Sum(p => p.AmountCents).ShouldBe(0);
            loaded.Postings.ShouldAllBe(p => p.Id > 0);
        }
    }

    [Fact]
    public async Task Database_rejects_negative_balance_on_customer_account()
    {
        Cpf.TryParse(TestUsers.NextCpf(), out var cpf).ShouldBeTrue();
        var user = User.Create("Cliente", cpf!, $"{Guid.NewGuid():N}@walletledger.dev", "h", Role.Customer, Now);
        var account = Account.OpenForCustomer(user.Id, Now);

        await using var db = NewContext();
        db.Users.Add(user);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        // Simula um bug que passasse por cima do domínio e gravasse direto no banco.
        var ex = await Should.ThrowAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE accounts SET balance_cents = -1 WHERE id = {account.Id}"));

        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Duplicate_email_violates_unique_constraint()
    {
        Cpf.TryParse(TestUsers.NextCpf(), out var cpf1).ShouldBeTrue();
        Cpf.TryParse(TestUsers.NextCpf(), out var cpf2).ShouldBeTrue();

        await using var db = NewContext();
        db.Users.Add(User.Create("Primeira", cpf1!, "dup@teste.dev", "h", Role.Customer, Now));
        await db.SaveChangesAsync();

        db.Users.Add(User.Create("Segunda", cpf2!, "DUP@teste.dev", "h", Role.Customer, Now));
        var ex = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        ex.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }
}
