using System.Net;
using System.Net.Http.Json;
using Shouldly;
using WalletLedger.Domain.Pix;

namespace WalletLedger.IntegrationTests.Pix;

[Collection("api")]
public sealed class ChargeTests(ApiFactory factory)
{
    private sealed record KeyResponse(Guid Id, string Type, string Value);

    private sealed record ChargeResponse(string Payload);

    private static async Task<KeyResponse> CreateEvpAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/pix/keys", new { type = "evp" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KeyResponse>())!;
    }

    [Fact]
    public async Task Charge_returns_valid_br_code_with_key_and_amount()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory, "Gabriela Souza Lima");
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);
        var key = await CreateEvpAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/pix/charges", new { keyId = key.Id, amountCents = 4_990 });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = (await response.Content.ReadFromJsonAsync<ChargeResponse>())!.Payload;
        var parsed = BrCode.Parse(payload);
        parsed.PixKey.ShouldBe(key.Value);
        parsed.AmountCents.ShouldBe(4_990);
        payload.ShouldContain("Gabriela Souza Lima");
    }

    [Fact]
    public async Task Charge_without_amount_leaves_amount_open()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);
        var key = await CreateEvpAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/pix/charges", new { keyId = key.Id });

        BrCode.Parse((await response.Content.ReadFromJsonAsync<ChargeResponse>())!.Payload).AmountCents.ShouldBeNull();
    }

    [Fact]
    public async Task Charge_with_other_users_key_returns_404()
    {
        var owner = await TestUsers.RegisterAndLoginAsync(factory);
        var key = await CreateEvpAsync(factory.CreateAuthenticatedClient(owner.Tokens.AccessToken));
        var other = await TestUsers.RegisterAndLoginAsync(factory);

        var response = await factory.CreateAuthenticatedClient(other.Tokens.AccessToken)
            .PostAsJsonAsync("/api/v1/pix/charges", new { keyId = key.Id, amountCents = 100 });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10_000_000_001)]
    public async Task Charge_amount_out_of_range_returns_400(long amountCents)
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);
        var key = await CreateEvpAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/pix/charges", new { keyId = key.Id, amountCents });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
