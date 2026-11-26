using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WalletLedger.Domain.Common;
using WalletLedger.Infrastructure.Persistence;

namespace WalletLedger.Infrastructure.Idempotency;

/// <summary>Apaga, de hora em hora, as chaves de idempotência com mais de 24 horas.</summary>
public sealed class IdempotencyCleanupService(IServiceScopeFactory scopes, TimeProvider clock) : BackgroundService
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var cutoff = clock.GetUtcNow() - Retention;

        return await db.Set<IdempotencyRecord>()
            .Where(k => k.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PurgeAsync(stoppingToken);
        }
    }
}
