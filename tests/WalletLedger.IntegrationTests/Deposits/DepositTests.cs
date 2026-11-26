using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Ledger;
using WalletLedger.Infrastructure.Persistence;

namespace WalletLedger.IntegrationTests.Deposits;

[Collection("api")]
public sealed class DepositTests(ApiFactory factory)
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private sealed record DepositResponse(Guid TransactionId, long BalanceCents);

    private static Task<HttpResponseMessage> DepositAsync(HttpClient client, long amountCents, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/deposits/simulated")
        {
            Content = JsonContent.Create(new { amountCents }),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Deposit_creates_balanced_postings_against_settlement_account()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);

        var response = await DepositAsync(client, 12_345);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<DepositResponse>())!;
        body.BalanceCents.ShouldBe(12_345);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var tx = await db.Transactions.Include(t => t.Postings).SingleAsync(t => t.Id == body.TransactionId);
        var settlement = await db.Accounts.SingleAsync(a => a.SystemCode == Account.SettlementAccountCode);

        tx.Type.ShouldBe(TransactionType.Deposit);
        tx.Postings.Single(p => p.AccountId == user.AccountId).AmountCents.ShouldBe(12_345);
        tx.Postings.Single(p => p.AccountId == settlement.Id).AmountCents.ShouldBe(-12_345);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000_000_001)]
    public async Task Deposit_amount_out_of_range_returns_400(long amountCents)
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);

        var response = await DepositAsync(factory.CreateAuthenticatedClient(user.Tokens.AccessToken), amountCents);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deposit_above_100000_returns_422_deposit_limit()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);

        var response = await DepositAsync(factory.CreateAuthenticatedClient(user.Tokens.AccessToken), 100_001);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("deposit_limit");
    }

    [Fact]
    public async Task Daily_deposit_cap_500000_resets_at_midnight_brasilia()
    {
        // Leva o relógio para as 23h50 (Brasília) do próximo dia. O relógio só anda para a frente.
        var nowBrasilia = TimeZoneInfo.ConvertTime(factory.Clock.GetUtcNow(), Brasilia);
        var target = new DateTimeOffset(nowBrasilia.Date.AddDays(1).AddHours(23).AddMinutes(50), nowBrasilia.Offset);
        factory.Clock.SetUtcNow(target.ToUniversalTime());

        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);

        for (var i = 0; i < 5; i++)
        {
            (await DepositAsync(client, 100_000)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var sixth = await DepositAsync(client, 1);
        sixth.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await sixth.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("deposit_limit");

        // 00h10 do dia seguinte: o limite zera.
        factory.Clock.Advance(TimeSpan.FromMinutes(20));
        var tokens = await TestUsers.LoginAsync(factory.CreateClient(), user.Email, user.Password);
        (await DepositAsync(factory.CreateAuthenticatedClient(tokens.AccessToken), 1)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Operator_cannot_use_simulated_deposit()
    {
        var tokens = await TestUsers.LoginAsync(factory.CreateClient(), "operador@walletledger.dev", "Demo@1234");

        var response = await DepositAsync(factory.CreateAuthenticatedClient(tokens.AccessToken), 100);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
