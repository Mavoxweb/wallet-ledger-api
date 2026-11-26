using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using WalletLedger.Application.Auth;
using WalletLedger.Application.Deposits;

namespace WalletLedger.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<TokenIssuer>();
        services.AddScoped<RegisterHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<SimulatedDepositHandler>();

        return services;
    }
}
