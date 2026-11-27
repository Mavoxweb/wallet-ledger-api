using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WalletLedger.Application.Abstractions;
using WalletLedger.Infrastructure.Persistence.Seed;

namespace WalletLedger.Infrastructure.Persistence;

/// <summary>
/// Aplica as migrations e os dados iniciais antes de o servidor aceitar requisições.
/// Fica num serviço para a geração do contrato no build não precisar de banco.
/// </summary>
internal sealed class DatabaseInitializer(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        await db.Database.MigrateAsync(cancellationToken);
        await SeedData.EnsureSystemAccountsAsync(db, clock, cancellationToken);

        if (configuration.GetValue<bool>("Seed:Demo"))
        {
            await DemoSeed.EnsureDemoDataAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher>(), clock, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
