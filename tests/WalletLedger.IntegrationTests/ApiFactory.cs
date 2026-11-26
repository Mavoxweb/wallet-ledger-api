using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
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

    /// <summary>
    /// Relógio da API durante os testes. Começa no horário real e só anda quando um
    /// teste mandar (por exemplo, para expirar um token). Nunca volta no tempo.
    /// </summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public string ConnectionString => _db.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "chave-de-teste-com-pelo-menos-32-bytes-de-tamanho");
        builder.UseSetting("Seed:Demo", "true");
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5000");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
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
