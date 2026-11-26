using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Ledger;
using WalletLedger.Infrastructure.Persistence;
using static WalletLedger.IntegrationTests.Transfers.TransferFixtures;

namespace WalletLedger.IntegrationTests.Transfers;

[Collection("api")]
public sealed class TransferTests(ApiFactory factory)
{
    private sealed record TransferResponse(Guid TransactionId, long AmountCents, string RecipientMaskedName, long BalanceCents, DateTimeOffset CreatedAt);

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Transfer_moves_money_and_creates_balanced_postings()
    {
        var payer = await FundedCustomerAsync(factory, 10_000);
        var payee = await FundedCustomerAsync(factory, 0, "Diego Ramos Alves");

        var response = await TransferAsync(payer.Client, "evp", payee.EvpKey, 2_500);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<TransferResponse>())!;
        body.AmountCents.ShouldBe(2_500);
        body.BalanceCents.ShouldBe(7_500);
        body.RecipientMaskedName.ShouldBe("Diego R. A.");

        (await BalanceAsync(factory, payer.User.AccountId)).ShouldBe(7_500);
        (await BalanceAsync(factory, payee.User.AccountId)).ShouldBe(2_500);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var tx = await db.Transactions.Include(t => t.Postings).SingleAsync(t => t.Id == body.TransactionId);
        tx.Type.ShouldBe(TransactionType.Transfer);
        tx.Postings.Single(p => p.AccountId == payer.User.AccountId).AmountCents.ShouldBe(-2_500);
        tx.Postings.Single(p => p.AccountId == payee.User.AccountId).AmountCents.ShouldBe(2_500);
    }

    [Fact]
    public async Task Transfer_to_email_key_in_different_case_works()
    {
        var tokens = await TestUsers.LoginAsync(factory.CreateClient(), "ana@walletledger.dev", "Demo@1234");
        var ana = factory.CreateAuthenticatedClient(tokens.AccessToken);

        var response = await TransferAsync(ana, "email", "  Bruno@WalletLedger.dev ", 100);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Transfer_with_insufficient_funds_returns_422_and_writes_nothing()
    {
        var payer = await FundedCustomerAsync(factory, 1_000);
        var payee = await FundedCustomerAsync(factory, 0);

        var response = await TransferAsync(payer.Client, "evp", payee.EvpKey, 1_001);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).ShouldBe("insufficient_funds");
        (await BalanceAsync(factory, payer.User.AccountId)).ShouldBe(1_000);
        (await PostingCountAsync(factory, payee.User.AccountId)).ShouldBe(0);
    }

    [Fact]
    public async Task Transfer_of_entire_balance_is_allowed()
    {
        var payer = await FundedCustomerAsync(factory, 1_000);
        var payee = await FundedCustomerAsync(factory, 0);

        (await TransferAsync(payer.Client, "evp", payee.EvpKey, 1_000)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await BalanceAsync(factory, payer.User.AccountId)).ShouldBe(0);
    }

    [Fact]
    public async Task Transfer_to_own_key_returns_422_self_transfer()
    {
        var payer = await FundedCustomerAsync(factory, 1_000);

        var response = await TransferAsync(payer.Client, "evp", payer.EvpKey, 100);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).ShouldBe("self_transfer");
    }

    [Theory]
    [InlineData(true, AccountStatus.Blocked)]
    [InlineData(true, AccountStatus.Closed)]
    [InlineData(false, AccountStatus.Blocked)]
    [InlineData(false, AccountStatus.Closed)]
    public async Task Transfer_from_or_to_inactive_account_returns_422_and_writes_nothing(bool blockPayer, AccountStatus status)
    {
        var payer = await FundedCustomerAsync(factory, 1_000);
        var payee = await FundedCustomerAsync(factory, 0);
        await SetStatusAsync(factory, blockPayer ? payer.User.AccountId : payee.User.AccountId, status);

        var response = await TransferAsync(payer.Client, "evp", payee.EvpKey, 100);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).ShouldBe("account_not_active");
        (await BalanceAsync(factory, payer.User.AccountId)).ShouldBe(1_000);
        (await PostingCountAsync(factory, payee.User.AccountId)).ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000_000_001)]
    public async Task Transfer_amount_out_of_range_returns_400(long amountCents)
    {
        var payer = await FundedCustomerAsync(factory, 1_000);
        var payee = await FundedCustomerAsync(factory, 0);

        var response = await TransferAsync(payer.Client, "evp", payee.EvpKey, amountCents);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Transfer_description_longer_than_140_returns_400()
    {
        var payer = await FundedCustomerAsync(factory, 1_000);
        var payee = await FundedCustomerAsync(factory, 0);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transfers/pix")
        {
            Content = JsonContent.Create(new { keyType = "evp", keyValue = payee.EvpKey, amountCents = 100, description = new string('x', 141) }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        (await payer.Client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Transfer_to_unknown_key_returns_404()
    {
        var payer = await FundedCustomerAsync(factory, 1_000);

        var response = await TransferAsync(payer.Client, "evp", Guid.NewGuid().ToString(), 100);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Repeated_transfer_with_same_key_moves_money_once()
    {
        var payer = await FundedCustomerAsync(factory, 10_000);
        var payee = await FundedCustomerAsync(factory, 0);
        var key = Guid.NewGuid().ToString();

        (await TransferAsync(payer.Client, "evp", payee.EvpKey, 3_000, key)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await TransferAsync(payer.Client, "evp", payee.EvpKey, 3_000, key)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await BalanceAsync(factory, payer.User.AccountId)).ShouldBe(7_000);
        (await BalanceAsync(factory, payee.User.AccountId)).ShouldBe(3_000);
    }
}
