using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WalletLedger.Infrastructure.Persistence;
using static WalletLedger.IntegrationTests.Transfers.TransferFixtures;

namespace WalletLedger.IntegrationTests.Statements;

[Collection("api")]
public sealed class StatementTests(ApiFactory factory)
{
    private sealed record AccountResponse(Guid AccountId, string Status, long BalanceCents);

    private sealed record StatementItem(
        long PostingId,
        Guid TransactionId,
        string Type,
        string Direction,
        long AmountCents,
        string? CounterpartyMaskedName,
        string? Description,
        DateTimeOffset CreatedAt);

    private sealed record StatementPage(StatementItem[] Items, string? NextCursor);

    private static async Task DepositAsync(HttpClient client, long amountCents)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/deposits/simulated")
        {
            Content = JsonContent.Create(new { amountCents }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static async Task<StatementPage> PageAsync(HttpClient client, int limit, string? cursor = null)
    {
        var url = $"/api/v1/account/statement?limit={limit}" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
        return (await client.GetFromJsonAsync<StatementPage>(url))!;
    }

    [Fact]
    public async Task Statement_returns_newest_first_with_stable_cursor()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);

        // Valores 1..25 centavos: o valor identifica a ordem de criação.
        for (var i = 1; i <= 25; i++)
        {
            await DepositAsync(client, i);
        }

        var page1 = await PageAsync(client, 10);

        // Um lançamento novo chega entre a página 1 e a 2.
        await DepositAsync(client, 999);

        var page2 = await PageAsync(client, 10, page1.NextCursor);
        var page3 = await PageAsync(client, 10, page2.NextCursor);

        page1.Items.Select(x => x.AmountCents).ShouldBe(Enumerable.Range(16, 10).Reverse().Select(x => (long)x));
        page2.Items.Select(x => x.AmountCents).ShouldBe(Enumerable.Range(6, 10).Reverse().Select(x => (long)x));
        page3.Items.Select(x => x.AmountCents).ShouldBe(Enumerable.Range(1, 5).Reverse().Select(x => (long)x));
        page3.NextCursor.ShouldBeNull();

        page1.Items.ShouldAllBe(x => x.Type == "deposit" && x.Direction == "in" && x.CounterpartyMaskedName == null);
        page1.Items[0].Description.ShouldBe("Depósito simulado");
    }

    [Fact]
    public async Task Transfer_appears_as_out_for_payer_and_in_for_payee_with_counterparty()
    {
        var payer = await FundedCustomerAsync(factory, 5_000, "Elisa Torres Prado");
        var payee = await FundedCustomerAsync(factory, 0, "Fabio Nunes Costa");
        (await TransferAsync(payer.Client, "evp", payee.EvpKey, 1_234)).EnsureSuccessStatusCode();

        var payerTop = (await PageAsync(payer.Client, 1)).Items.Single();
        var payeeTop = (await PageAsync(payee.Client, 1)).Items.Single();

        payerTop.Type.ShouldBe("transfer");
        payerTop.Direction.ShouldBe("out");
        payerTop.AmountCents.ShouldBe(1_234);
        payerTop.CounterpartyMaskedName.ShouldBe("Fabio N. C.");
        payerTop.Description.ShouldBe("Teste");

        payeeTop.Direction.ShouldBe("in");
        payeeTop.AmountCents.ShouldBe(1_234);
        payeeTop.CounterpartyMaskedName.ShouldBe("Elisa T. P.");
        payeeTop.TransactionId.ShouldBe(payerTop.TransactionId);
    }

    [Fact]
    public async Task Statement_only_shows_own_account()
    {
        var ana = await TestUsers.RegisterAndLoginAsync(factory);
        var bia = await TestUsers.RegisterAndLoginAsync(factory);
        var anaClient = factory.CreateAuthenticatedClient(ana.Tokens.AccessToken);
        await DepositAsync(anaClient, 777);

        var biaPage = await PageAsync(factory.CreateAuthenticatedClient(bia.Tokens.AccessToken), 50);

        biaPage.Items.ShouldBeEmpty();
        biaPage.NextCursor.ShouldBeNull();
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=10&cursor=!!!")]
    [InlineData("limit=10&cursor=bm90LXVtLW51bWVybw")]
    public async Task Invalid_cursor_or_limit_returns_400(string query)
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);

        var response = await factory.CreateAuthenticatedClient(user.Tokens.AccessToken).GetAsync($"/api/v1/account/statement?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Default_limit_is_20()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);
        for (var i = 0; i < 21; i++)
        {
            await DepositAsync(client, 10);
        }

        var page = (await client.GetFromJsonAsync<StatementPage>("/api/v1/account/statement"))!;

        page.Items.Length.ShouldBe(20);
        page.NextCursor.ShouldNotBeNull();
    }

    [Fact]
    public async Task Balance_matches_sum_of_postings()
    {
        var payer = await FundedCustomerAsync(factory, 8_000);
        var payee = await FundedCustomerAsync(factory, 0);
        (await TransferAsync(payer.Client, "evp", payee.EvpKey, 3_210)).EnsureSuccessStatusCode();

        var account = (await payer.Client.GetFromJsonAsync<AccountResponse>("/api/v1/account"))!;

        account.AccountId.ShouldBe(payer.User.AccountId);
        account.Status.ShouldBe("active");
        account.BalanceCents.ShouldBe(4_790);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        (await db.Postings.Where(p => p.AccountId == payer.User.AccountId).SumAsync(p => p.AmountCents)).ShouldBe(4_790);
    }
}
