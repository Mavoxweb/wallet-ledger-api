using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using WalletLedger.Application.Auth;
using WalletLedger.Application.Backoffice;
using WalletLedger.Application.Deposits;
using WalletLedger.Application.Pix;
using WalletLedger.Application.Statements;
using WalletLedger.Application.Transfers;

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
        services.AddScoped<CreateChargeHandler>();
        services.AddScoped<TransferByPixKeyHandler>();
        services.AddScoped<GetBalanceHandler>();
        services.AddScoped<GetStatementHandler>();
        services.AddScoped<SearchAccountsHandler>();
        services.AddScoped<GetAccountDetailHandler>();
        services.AddScoped<BackofficeStatementHandler>();
        services.AddScoped<BlockAccountHandler>();
        services.AddScoped<UnblockAccountHandler>();

        return services;
    }
}
