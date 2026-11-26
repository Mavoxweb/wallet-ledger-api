using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WalletLedger.Infrastructure.Persistence;
using static WalletLedger.IntegrationTests.Transfers.TransferFixtures;

namespace WalletLedger.IntegrationTests.Transfers;

[Collection("api")]
public sealed class TransferConcurrencyTests(ApiFactory factory)
{
    /// <summary>
    /// O teste que responde "como você impede saldo negativo com transferências
    /// simultâneas?". 100 PIX disparados ao mesmo tempo, metade A→B e metade B→A
    /// (o cenário clássico de deadlock), e as invariantes do razão conferidas no fim.
    /// </summary>
    [Fact]
    public async Task Parallel_crossed_transfers_never_overdraw_and_ledger_stays_balanced()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 10_000);

        var requests = Enumerable.Range(0, 100).Select(i => i % 2 == 0
            ? TransferAsync(a.Client, "evp", b.EvpKey, 300)
            : TransferAsync(b.Client, "evp", a.EvpKey, 300));
        var responses = await Task.WhenAll(requests);

        // Toda resposta é sucesso ou recusa por saldo; nunca erro interno nem deadlock.
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.UnprocessableEntity);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBeGreaterThan(0);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var ids = new[] { a.User.AccountId, b.User.AccountId };
        var accounts = await db.Accounts.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync();

        // 1. Nenhum saldo negativo.
        accounts.ShouldAllBe(x => x.BalanceCents >= 0);

        // 2. Dinheiro não foi criado nem destruído entre as duas contas.
        accounts.Sum(x => x.BalanceCents).ShouldBe(20_000);

        // 3. O saldo em cache de cada conta bate com a soma dos lançamentos dela.
        foreach (var account in accounts)
        {
            var ledger = await db.Postings.Where(p => p.AccountId == account.Id).SumAsync(p => p.AmountCents);
            account.BalanceCents.ShouldBe(ledger);
        }

        // 4. O razão inteiro continua somando zero.
        (await db.Postings.SumAsync(p => p.AmountCents)).ShouldBe(0);
    }
}
