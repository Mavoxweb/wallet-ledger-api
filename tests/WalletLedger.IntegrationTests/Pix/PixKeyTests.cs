using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace WalletLedger.IntegrationTests.Pix;

[Collection("api")]
public sealed class PixKeyTests(ApiFactory factory)
{
    private sealed record PixKeyResponse(Guid Id, string Type, string Value, DateTimeOffset CreatedAt);

    private sealed record LookupResponse(string KeyType, string MaskedName, string MaskedCpf);

    private static int _phoneSeed = 10_000_000;

    private static string NewPhone() => $"(21) 9{Interlocked.Increment(ref _phoneSeed):D8}";

    private static Task<HttpResponseMessage> CreateKeyAsync(HttpClient client, string type, string? value = null) =>
        client.PostAsJsonAsync("/api/v1/pix/keys", new { type, value });

    [Fact]
    public async Task Create_list_and_delete_keys()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);

        var created = await CreateKeyAsync(client, "email", $"  {user.Email.ToUpperInvariant()} ");
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var key = (await created.Content.ReadFromJsonAsync<PixKeyResponse>())!;
        key.Type.ShouldBe("email");
        key.Value.ShouldBe(user.Email);

        var list = await client.GetFromJsonAsync<PixKeyResponse[]>("/api/v1/pix/keys");
        list!.Select(k => k.Id).ShouldBe([key.Id]);

        (await client.DeleteAsync($"/api/v1/pix/keys/{key.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<PixKeyResponse[]>("/api/v1/pix/keys"))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Creating_equivalent_key_in_another_format_returns_409()
    {
        var ana = await TestUsers.RegisterAndLoginAsync(factory);
        var bia = await TestUsers.RegisterAndLoginAsync(factory);
        var digits = new string(NewPhone().Where(char.IsDigit).ToArray());

        (await CreateKeyAsync(factory.CreateAuthenticatedClient(ana.Tokens.AccessToken), "phone", $"({digits[..2]}) {digits[2..7]}-{digits[7..]}"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await CreateKeyAsync(factory.CreateAuthenticatedClient(bia.Tokens.AccessToken), "phone", $"+55{digits}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("pix_key_taken");
    }

    [Fact]
    public async Task Sixth_key_returns_422_pix_key_limit()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);
        for (var i = 0; i < 5; i++)
        {
            (await CreateKeyAsync(client, "evp")).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var sixth = await CreateKeyAsync(client, "evp");

        sixth.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await sixth.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("pix_key_limit");
    }

    [Fact]
    public async Task Parallel_creates_never_exceed_five_keys()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => CreateKeyAsync(client, "evp")));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(5);
        responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).ShouldBe(3);
    }

    [Fact]
    public async Task Invalid_key_value_returns_422_invalid_pix_key()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);

        var response = await CreateKeyAsync(factory.CreateAuthenticatedClient(user.Tokens.AccessToken), "phone", "123");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("invalid_pix_key");
    }

    [Fact]
    public async Task Deleting_other_users_key_returns_404()
    {
        var owner = await TestUsers.RegisterAndLoginAsync(factory);
        var other = await TestUsers.RegisterAndLoginAsync(factory);
        var key = (await (await CreateKeyAsync(factory.CreateAuthenticatedClient(owner.Tokens.AccessToken), "evp"))
            .Content.ReadFromJsonAsync<PixKeyResponse>())!;

        var response = await factory.CreateAuthenticatedClient(other.Tokens.AccessToken).DeleteAsync($"/api/v1/pix/keys/{key.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lookup_returns_masked_name_and_cpf_only()
    {
        var owner = await TestUsers.RegisterAndLoginAsync(factory, "Carla Mendes Rocha");
        var phone = NewPhone();
        (await CreateKeyAsync(factory.CreateAuthenticatedClient(owner.Tokens.AccessToken), "phone", phone)).EnsureSuccessStatusCode();
        var payer = await TestUsers.RegisterAndLoginAsync(factory);

        var response = await factory.CreateAuthenticatedClient(payer.Tokens.AccessToken)
            .GetAsync($"/api/v1/pix/keys/lookup?type=phone&value={Uri.EscapeDataString(phone)}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<LookupResponse>(raw, JsonSerializerOptions.Web)!;
        body.KeyType.ShouldBe("phone");
        body.MaskedName.ShouldBe("Carla M. R.");
        body.MaskedCpf.ShouldStartWith("***.");
        raw.ShouldNotContain(owner.Email);
        raw.ShouldNotContain("Mendes");
    }

    [Fact]
    public async Task Lookup_unknown_key_returns_404()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);

        var response = await factory.CreateAuthenticatedClient(user.Tokens.AccessToken)
            .GetAsync("/api/v1/pix/keys/lookup?type=email&value=ninguem%40lugar.dev");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Demo_customers_have_email_keys()
    {
        var tokens = await TestUsers.LoginAsync(factory.CreateClient(), "ana@walletledger.dev", "Demo@1234");

        var response = await factory.CreateAuthenticatedClient(tokens.AccessToken)
            .GetAsync("/api/v1/pix/keys/lookup?type=email&value=bruno%40walletledger.dev");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<LookupResponse>())!.MaskedName.ShouldBe("Bruno C. D.");
    }
}
