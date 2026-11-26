using Shouldly;

namespace WalletLedger.IntegrationTests.Cors;

/// <summary>
/// O app Flutter rodando no navegador (build web) fica em outra origem que a API,
/// então o navegador pergunta antes (preflight OPTIONS) se pode chamar.
/// </summary>
[Collection("api")]
public sealed class CorsTests(ApiFactory factory)
{
    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/transfers/pix");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,idempotency-key");
        return request;
    }

    [Fact]
    public async Task Cors_preflight_from_allowed_origin_returns_allow_origin_header()
    {
        var response = await factory.CreateClient().SendAsync(Preflight("http://localhost:5000"));

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["http://localhost:5000"]);
        string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant()
            .ShouldContain("idempotency-key");
    }

    [Fact]
    public async Task Cors_preflight_from_unknown_origin_gets_no_allow_origin_header()
    {
        var response = await factory.CreateClient().SendAsync(Preflight("https://site-qualquer.example"));

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}
