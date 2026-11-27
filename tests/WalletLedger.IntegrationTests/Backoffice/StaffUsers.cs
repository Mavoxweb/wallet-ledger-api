namespace WalletLedger.IntegrationTests.Backoffice;

/// <summary>Logins da equipe de operação criados pelo seed de demonstração.</summary>
public static class StaffUsers
{
    public const string OperatorEmail = "operador@walletledger.dev";
    public const string AdminEmail = "admin@walletledger.dev";
    public const string Password = "Demo@1234";

    public static Task<HttpClient> OperatorAsync(ApiFactory factory) => LoginAsync(factory, OperatorEmail);

    public static Task<HttpClient> AdminAsync(ApiFactory factory) => LoginAsync(factory, AdminEmail);

    private static async Task<HttpClient> LoginAsync(ApiFactory factory, string email)
    {
        var tokens = await TestUsers.LoginAsync(factory.CreateClient(), email, Password);
        return factory.CreateAuthenticatedClient(tokens.AccessToken);
    }
}
