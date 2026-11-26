using System.Net;
using Shouldly;

namespace WalletLedger.IntegrationTests.Health;

[Collection("api")]
public sealed class HealthTests(ApiFactory factory)
{
    [Fact]
    public async Task Get_health_returns_200_and_reports_database_healthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Healthy");
    }
}
