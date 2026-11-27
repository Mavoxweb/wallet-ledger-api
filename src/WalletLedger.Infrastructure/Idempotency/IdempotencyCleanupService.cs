using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WalletLedger.Domain.Common;
using WalletLedger.Infrastructure.Persistence;

namespace WalletLedger.Infrastructure.Idempotency;

/// <summary>Apaga, de hora em hora, as chaves de idempotência com mais de 24 horas.</summary>
public sealed partial class IdempotencyCleanupService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<IdempotencyCleanupService> logger) : BackgroundService
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

    /// <summary>
    /// Nunca lança: uma exceção num BackgroundService derruba a aplicação inteira.
    /// </summary>
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await PurgeAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPurgeFailed(logger, ex);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha na limpeza de chaves de idempotência; nova tentativa na próxima rodada")]
    private static partial void LogPurgeFailed(ILogger logger, Exception exception);
}
