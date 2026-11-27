using System.Net;
using System.Net.Http.Json;
using Shouldly;
using static WalletLedger.IntegrationTests.Transfers.TransferFixtures;

namespace WalletLedger.IntegrationTests.Backoffice;

[Collection("api")]
public sealed class BackofficeStatementTests(ApiFactory factory)
{
    private sealed record Item(
        long PostingId,
        Guid TransactionId,
        string Type,
        string Status,
        string Direction,
        long AmountCents,
        string? CounterpartyMaskedName,
        string? Description,
        DateTimeOffset CreatedAt,
        bool Reversible);

    private sealed record Page(Item[] Items, string? NextCursor);

    private sealed record Transfer(Guid TransactionId);

    [Fact]
    public async Task Extrato_de_outra_conta_marca_reversible()
    {
        var a = await FundedCustomerAsync(factory, 10_000, "Alice Pereira");
        var b = await FundedCustomerAsync(factory, 100, "Bruno Teste");
        var pix = await TransferAsync(a.Client, "evp", b.EvpKey, 2_500);
        pix.EnsureSuccessStatusCode();
        var transferId = (await pix.Content.ReadFromJsonAsync<Transfer>())!.TransactionId;
        var client = await StaffUsers.OperatorAsync(factory);

        var page = (await client.GetFromJsonAsync<Page>($"/api/v1/backoffice/accounts/{b.User.AccountId}/statement"))!;

        var item = page.Items.Single(i => i.TransactionId == transferId);
        item.Type.ShouldBe("transfer");
        item.Status.ShouldBe("completed");
        item.Direction.ShouldBe("in");
        item.AmountCents.ShouldBe(2_500);
        item.CounterpartyMaskedName.ShouldNotBeNull();
        item.Reversible.ShouldBeTrue();
        page.Items.ShouldContain(i => i.Type == "deposit" && i.Reversible);
    }

    [Fact]
    public async Task Extrato_pagina_com_cursor()
    {
        var a = await FundedCustomerAsync(factory, 300_000);
        var client = await StaffUsers.OperatorAsync(factory);

        var first = (await client.GetFromJsonAsync<Page>($"/api/v1/backoffice/accounts/{a.User.AccountId}/statement?limit=2"))!;
        var second = (await client.GetFromJsonAsync<Page>(
            $"/api/v1/backoffice/accounts/{a.User.AccountId}/statement?limit=2&cursor={first.NextCursor}"))!;

        first.Items.Length.ShouldBe(2);
        first.NextCursor.ShouldNotBeNull();
        second.Items.Length.ShouldBe(1);
        second.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Extrato_de_conta_inexistente_da_404()
    {
        var client = await StaffUsers.OperatorAsync(factory);

        var response = await client.GetAsync($"/api/v1/backoffice/accounts/{Guid.NewGuid()}/statement");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cliente_nao_le_extrato_de_outro_403()
    {
        var a = await TestUsers.RegisterAndLoginAsync(factory);
        var b = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(a.Tokens.AccessToken);

        var response = await client.GetAsync($"/api/v1/backoffice/accounts/{b.AccountId}/statement");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
