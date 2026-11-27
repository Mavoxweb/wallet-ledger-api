using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace WalletLedger.IntegrationTests.Backoffice;

[Collection("api")]
public sealed class BackofficeAccountsTests(ApiFactory factory)
{
    private sealed record Summary(Guid AccountId, string Number, string HolderName, string HolderEmail, string Status, long BalanceCents, DateTimeOffset OpenedAt);

    private sealed record Page(Summary[] Items, string? NextCursor);

    private sealed record Key(Guid Id, string Type, string Value);

    private sealed record Detail(
        Guid AccountId,
        string Number,
        string HolderName,
        string HolderEmail,
        string HolderCpfMasked,
        string Status,
        long BalanceCents,
        DateTimeOffset OpenedAt,
        Key[] PixKeys);

    private sealed record Registered(Guid UserId, Guid AccountId);

    private async Task<(Guid AccountId, string Email, string Cpf, string Name)> RegisterAsync(string? name = null)
    {
        name ??= $"Cliente {Guid.NewGuid():N}"[..20];
        var email = TestUsers.NewEmail();
        var cpf = TestUsers.NextCpf();
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new { name, cpf, email, password = TestUsers.Password });
        response.EnsureSuccessStatusCode();
        var registered = (await response.Content.ReadFromJsonAsync<Registered>())!;
        return (registered.AccountId, email, cpf, name);
    }

    private static string Url(string? q = null, int? limit = null, string? cursor = null, string? status = null)
    {
        var parts = new List<string>();
        if (q is not null)
        {
            parts.Add($"q={Uri.EscapeDataString(q)}");
        }

        if (status is not null)
        {
            parts.Add($"status={status}");
        }

        if (limit is not null)
        {
            parts.Add($"limit={limit}");
        }

        if (cursor is not null)
        {
            parts.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        return "/api/v1/backoffice/accounts" + (parts.Count > 0 ? "?" + string.Join('&', parts) : string.Empty);
    }

    [Fact]
    public async Task Cliente_recebe_403_no_backoffice()
    {
        var customer = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(customer.Tokens.AccessToken);

        var response = await client.GetAsync(Url());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sem_login_recebe_401()
    {
        var response = await factory.CreateClient().GetAsync(Url());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Busca_por_cpf_formatado_encontra_a_conta()
    {
        var created = await RegisterAsync();
        var client = await StaffUsers.OperatorAsync(factory);
        var formatted = $"{created.Cpf[..3]}.{created.Cpf[3..6]}.{created.Cpf[6..9]}-{created.Cpf[9..]}";

        var page = (await client.GetFromJsonAsync<Page>(Url(formatted)))!;

        var item = page.Items.ShouldHaveSingleItem();
        item.AccountId.ShouldBe(created.AccountId);
        item.HolderName.ShouldBe(created.Name);
    }

    [Fact]
    public async Task Busca_por_email_ignora_maiusculas_e_espacos()
    {
        var created = await RegisterAsync();
        var client = await StaffUsers.OperatorAsync(factory);

        var page = (await client.GetFromJsonAsync<Page>(Url($"  {created.Email.ToUpperInvariant()} ")))!;

        page.Items.ShouldHaveSingleItem().AccountId.ShouldBe(created.AccountId);
    }

    [Fact]
    public async Task Busca_por_nome_parcial()
    {
        var created = await RegisterAsync($"Zuleica {Guid.NewGuid():N}"[..20]);
        var client = await StaffUsers.OperatorAsync(factory);

        var page = (await client.GetFromJsonAsync<Page>(Url(created.Name[..14].ToLowerInvariant())))!;

        page.Items.Select(i => i.AccountId).ShouldContain(created.AccountId);
    }

    [Fact]
    public async Task Busca_por_final_da_conta()
    {
        var created = await RegisterAsync();
        var client = await StaffUsers.OperatorAsync(factory);
        var suffix = created.AccountId.ToString("N")[^4..];

        var page = (await client.GetFromJsonAsync<Page>(Url(suffix)))!;

        var item = page.Items.Single(i => i.AccountId == created.AccountId);
        item.Number.ShouldBe(suffix);
    }

    [Fact]
    public async Task Busca_vazia_lista_so_contas_de_cliente()
    {
        var client = await StaffUsers.OperatorAsync(factory);

        var page = (await client.GetFromJsonAsync<Page>(Url(limit: 100)))!;

        page.Items.ShouldNotBeEmpty();
        page.Items.ShouldAllBe(i => i.HolderEmail.Length > 0);
        page.Items.ShouldNotContain(i => i.HolderEmail == StaffUsers.OperatorEmail);
    }

    [Fact]
    public async Task Filtro_por_status()
    {
        var client = await StaffUsers.OperatorAsync(factory);

        var page = (await client.GetFromJsonAsync<Page>(Url(status: "active", limit: 100)))!;

        page.Items.ShouldNotBeEmpty();
        page.Items.ShouldAllBe(i => i.Status == "active");
    }

    [Fact]
    public async Task Paginacao_por_cursor_nao_repete_nem_pula()
    {
        var created = new List<Guid>();
        for (var i = 0; i < 25; i++)
        {
            created.Add((await RegisterAsync()).AccountId);
        }

        var client = await StaffUsers.OperatorAsync(factory);
        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = (await client.GetFromJsonAsync<Page>(Url(limit: 10, cursor: cursor)))!;
            page.Items.Length.ShouldBeLessThanOrEqualTo(10);
            seen.AddRange(page.Items.Select(i => i.AccountId));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.Count.ShouldBe(seen.Distinct().Count());
        created.ShouldAllBe(id => seen.Contains(id));
    }

    [Fact]
    public async Task Cursor_invalido_da_400()
    {
        var client = await StaffUsers.OperatorAsync(factory);

        var response = await client.GetAsync(Url(cursor: "nao-e-cursor"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Detalhe_mostra_cpf_mascarado_e_chaves()
    {
        var client = await StaffUsers.AdminAsync(factory);
        var ana = (await client.GetFromJsonAsync<Page>(Url("ana@walletledger.dev")))!.Items.ShouldHaveSingleItem();

        var detail = (await client.GetFromJsonAsync<Detail>($"/api/v1/backoffice/accounts/{ana.AccountId}"))!;

        detail.HolderName.ShouldBe("Ana Beatriz Lima");
        detail.HolderCpfMasked.ShouldBe("***.982.247-**");
        detail.Number.ShouldBe(ana.AccountId.ToString("N")[^4..]);
        detail.PixKeys.ShouldContain(k => k.Value == "ana@walletledger.dev");
    }

    [Fact]
    public async Task Detalhe_de_conta_inexistente_da_404()
    {
        var client = await StaffUsers.OperatorAsync(factory);

        var response = await client.GetAsync($"/api/v1/backoffice/accounts/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
