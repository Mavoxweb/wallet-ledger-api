using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WalletLedger.Application.Abstractions;
using WalletLedger.Infrastructure.Auth;
using WalletLedger.Infrastructure.Idempotency;
using WalletLedger.Infrastructure.Persistence;

namespace WalletLedger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");

        services.AddDbContext<WalletDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IWalletDbContext>(sp => sp.GetRequiredService<WalletDbContext>());

        // Relógio injetável: os testes trocam por um FakeTimeProvider.
        services.TryAddSingleton(TimeProvider.System);

        // A API nem sobe se a configuração do JWT estiver faltando ou fraca.
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();

        services.AddScoped<IIdempotentOperationRunner, IdempotentOperationRunner>();
        services.AddScoped<IAccountLocker, AccountLocker>();
        services.AddSingleton<IdempotencyCleanupService>();
        services.AddHostedService(sp => sp.GetRequiredService<IdempotencyCleanupService>());

        return services;
    }
}
