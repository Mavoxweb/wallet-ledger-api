using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using WalletLedger.Infrastructure.Idempotency;
using WalletLedger.Infrastructure.Persistence;

namespace WalletLedger.IntegrationTests.Idempotency;

public sealed class CleanupResilienceTests
{
    [Fact]
    public async Task Cleanup_round_survives_database_failure()
    {
        // Banco inacessível: simula o banco reiniciando no meio da madrugada.
        var services = new ServiceCollection()
            .AddDbContext<WalletDbContext>(o => o
                .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
                .UseSnakeCaseNamingConvention())
            .BuildServiceProvider();
        var cleanup = new IdempotencyCleanupService(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<IdempotencyCleanupService>.Instance);

        // Uma rodada que falha não pode lançar exceção: isso derrubaria a API inteira.
        var ok = await cleanup.RunOnceAsync(CancellationToken.None);

        ok.ShouldBeFalse();
    }
}
