using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace WalletLedger.IntegrationTests;

/// <summary>
/// Sobe a API em memória apontando para um PostgreSQL real, criado num contêiner
/// descartável. Cada execução da suíte começa com um banco limpo.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public string ConnectionString => _db.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
    }

    public HttpClient CreateAuthenticatedClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public Task InitializeAsync() => _db.StartAsync();

    public new Task DisposeAsync() => _db.DisposeAsync().AsTask();
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
