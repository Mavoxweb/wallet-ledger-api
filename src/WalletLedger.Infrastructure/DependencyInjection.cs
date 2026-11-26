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
    /// <param name="runStartupTasks">
    /// Falso quando a aplicação é carregada só para gerar o contrato OpenAPI no build:
    /// aí não há banco nem segredos, e as tarefas de inicialização são puladas.
    /// </param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool runStartupTasks = true)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");

        services.AddDbContext<WalletDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IWalletDbContext>(sp => sp.GetRequiredService<WalletDbContext>());

        if (runStartupTasks)
        {
            // Primeiro serviço a subir: banco migrado e dados iniciais antes de tudo.
            services.AddHostedService<DatabaseInitializer>();
        }

        // Relógio injetável: os testes trocam por um FakeTimeProvider.
        services.TryAddSingleton(TimeProvider.System);

        // A API nem sobe se a configuração do JWT estiver faltando ou fraca.
        var jwt = services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations();
        if (runStartupTasks)
        {
            jwt.ValidateOnStart();
        }
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();

        services.AddScoped<IIdempotentOperationRunner, IdempotentOperationRunner>();
        services.AddScoped<IAccountLocker, AccountLocker>();
        services.AddSingleton<IdempotencyCleanupService>();
        if (runStartupTasks)
        {
            services.AddHostedService(sp => sp.GetRequiredService<IdempotencyCleanupService>());
        }

        return services;
    }
}
