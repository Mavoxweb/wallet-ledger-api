using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using static WalletLedger.IntegrationTests.Backoffice.ReversalFixtures;
using static WalletLedger.IntegrationTests.Transfers.TransferFixtures;

namespace WalletLedger.IntegrationTests.Backoffice;

[Collection("api")]
public sealed class AuditTests(ApiFactory factory)
{
    private const string BlockReason = "Movimentação atípica em análise.";
    private const string UnblockReason = "Análise concluída sem irregularidade.";

    private sealed record Item(
        Guid Id,
        DateTimeOffset At,
        Guid ActorId,
        string ActorName,
        string ActorRole,
        string Action,
        Guid AccountId,
        string AccountNumber,
        string? AccountHolderName,
        Guid? TransactionId,
        string Reason,
        JsonElement Details);

    private sealed record Page(Item[] Items, string? NextCursor);

    private static Task<HttpResponseMessage> PostReasonAsync(HttpClient client, string url, string reason) =>
        client.PostAsJsonAsync(url, new { reason });

    [Fact]
    public async Task Lista_do_mais_recente_e_filtra_por_conta_e_acao()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 5_000);
        var admin = await StaffUsers.AdminAsync(factory);
        var url = $"/api/v1/backoffice/accounts/{a.User.AccountId}";
        (await PostReasonAsync(admin, $"{url}/block", BlockReason)).EnsureSuccessStatusCode();
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await PostReasonAsync(admin, $"{url}/unblock", UnblockReason)).EnsureSuccessStatusCode();
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await ReverseAsync(admin, pixId)).EnsureSuccessStatusCode();
        var operador = await StaffUsers.OperatorAsync(factory);

        var all = (await operador.GetFromJsonAsync<Page>($"/api/v1/backoffice/audit?accountId={a.User.AccountId}"))!;

        all.Items.Select(i => i.Action).ShouldBe(["transactionReversed", "accountUnblocked", "accountBlocked"]);
        var reversal = all.Items[0];
        reversal.TransactionId.ShouldBe(pixId);
        reversal.ActorName.ShouldNotBeNullOrWhiteSpace();
        reversal.ActorRole.ShouldBe("admin");
        reversal.AccountNumber.ShouldBe(a.User.AccountId.ToString("N")[^4..]);
        reversal.AccountHolderName.ShouldBe("Cliente Teste");
        reversal.Details.GetProperty("amountCents").GetInt64().ShouldBe(5_000);
        all.Items[2].Reason.ShouldBe(BlockReason);

        var onlyBlocks = (await operador.GetFromJsonAsync<Page>(
            $"/api/v1/backoffice/audit?accountId={a.User.AccountId}&action=accountBlocked"))!;
        onlyBlocks.Items.ShouldHaveSingleItem().Reason.ShouldBe(BlockReason);
    }

    [Fact]
    public async Task Pagina_com_cursor_sem_repetir()
    {
        var customer = await TestUsers.RegisterAndLoginAsync(factory);
        var admin = await StaffUsers.AdminAsync(factory);
        var url = $"/api/v1/backoffice/accounts/{customer.AccountId}";
        for (var i = 0; i < 3; i++)
        {
            (await PostReasonAsync(admin, $"{url}/block", BlockReason)).EnsureSuccessStatusCode();
            (await PostReasonAsync(admin, $"{url}/unblock", UnblockReason)).EnsureSuccessStatusCode();
        }

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var query = $"/api/v1/backoffice/audit?accountId={customer.AccountId}&limit=4" +
                (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = (await admin.GetFromJsonAsync<Page>(query))!;
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.Count.ShouldBe(6);
        seen.Distinct().Count().ShouldBe(6);
    }

    [Fact]
    public async Task Cliente_recebe_403_na_auditoria()
    {
        var customer = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(customer.Tokens.AccessToken);

        var response = await client.GetAsync("/api/v1/backoffice/audit");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cursor_invalido_da_400()
    {
        var admin = await StaffUsers.AdminAsync(factory);

        var response = await admin.GetAsync("/api/v1/backoffice/audit?cursor=xyz");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Estorno_aparece_na_trilha_das_duas_contas()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 2_000);
        var admin = await StaffUsers.AdminAsync(factory);
        (await ReverseAsync(admin, pixId)).EnsureSuccessStatusCode();

        foreach (var accountId in new[] { a.User.AccountId, b.User.AccountId })
        {
            var page = (await admin.GetFromJsonAsync<Page>($"/api/v1/backoffice/audit?accountId={accountId}"))!;
            page.Items.ShouldContain(i => i.TransactionId == pixId && i.Action == "transactionReversed");
        }
    }

    [Fact]
    public async Task Filtro_por_conta_nao_traz_estorno_de_outras_contas()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var outsider = await TestUsers.RegisterAndLoginAsync(factory);
        var pixId = await PixAsync(a.Client, b.EvpKey, 2_000);
        var admin = await StaffUsers.AdminAsync(factory);
        (await ReverseAsync(admin, pixId)).EnsureSuccessStatusCode();

        var page = (await admin.GetFromJsonAsync<Page>($"/api/v1/backoffice/audit?accountId={outsider.AccountId}"))!;

        page.Items.ShouldBeEmpty();
    }
}
