using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using WalletLedger.Application.Auth;
using WalletLedger.Application.Deposits;
using WalletLedger.Application.Pix;

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
        services.AddScoped<CreatePixKeyHandler>();
        services.AddScoped<ListPixKeysHandler>();
        services.AddScoped<DeletePixKeyHandler>();
        services.AddScoped<LookupPixKeyHandler>();

        return services;
    }
}
