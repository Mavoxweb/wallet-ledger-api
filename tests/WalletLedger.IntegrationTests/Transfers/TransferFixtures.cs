using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletLedger.Domain.Accounts;
using WalletLedger.Infrastructure.Persistence;

namespace WalletLedger.IntegrationTests.Transfers;

/// <summary>Cliente de teste com saldo e uma chave PIX aleatória.</summary>
public sealed record Funded(TestUser User, HttpClient Client, string EvpKey);

public static class TransferFixtures
{
    private sealed record KeyResponse(Guid Id, string Type, string Value);

    public static async Task<Funded> FundedCustomerAsync(ApiFactory factory, long balanceCents, string name = "Cliente Teste")
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory, name);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);

        // Depósitos de até R$ 1.000,00 cada, como a regra exige.
        for (var remaining = balanceCents; remaining > 0; remaining -= 100_000)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/deposits/simulated")
            {
                Content = JsonContent.Create(new { amountCents = Math.Min(remaining, 100_000) }),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            (await client.SendAsync(request)).EnsureSuccessStatusCode();
        }

        var key = await client.PostAsJsonAsync("/api/v1/pix/keys", new { type = "evp" });
        key.EnsureSuccessStatusCode();
        var evp = (await key.Content.ReadFromJsonAsync<KeyResponse>())!.Value;

        return new Funded(user, client, evp);
    }

    public static Task<HttpResponseMessage> TransferAsync(HttpClient client, string keyType, string keyValue, long amountCents, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transfers/pix")
        {
            Content = JsonContent.Create(new { keyType, keyValue, amountCents, description = "Teste" }),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    public static async Task<long> BalanceAsync(ApiFactory factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Accounts.Where(a => a.Id == accountId).Select(a => a.BalanceCents).SingleAsync();
    }

    public static async Task SetStatusAsync(ApiFactory factory, Guid accountId, AccountStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        await db.Accounts.Where(a => a.Id == accountId).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));
    }

    public static async Task<int> PostingCountAsync(ApiFactory factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Postings.CountAsync(p => p.AccountId == accountId);
    }
}
