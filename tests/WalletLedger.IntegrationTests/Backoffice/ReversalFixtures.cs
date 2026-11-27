using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletLedger.Domain.Audit;
using WalletLedger.Domain.Ledger;
using WalletLedger.Infrastructure.Persistence;

namespace WalletLedger.IntegrationTests.Backoffice;

public sealed record ReversalResult(Guid ReversalTransactionId, Guid OriginalTransactionId, long AmountCents, DateTimeOffset CreatedAt);

public static class ReversalFixtures
{
    public const string Reason = "Pix enviado por engano, confirmado com os dois clientes.";

    public static Task<HttpResponseMessage> ReverseAsync(HttpClient client, Guid transactionId, string? key = null, string reason = Reason)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/backoffice/transactions/{transactionId}/reversal")
        {
            Content = JsonContent.Create(new { reason }),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    public static async Task<Guid> PixAsync(HttpClient payer, string payeeKey, long amountCents)
    {
        var response = await Transfers.TransferFixtures.TransferAsync(payer, "evp", payeeKey, amountCents);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReversalTransferId>())!.TransactionId;
    }

    public static async Task<TransactionStatus> StatusAsync(ApiFactory factory, Guid transactionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Transactions.Where(t => t.Id == transactionId).Select(t => t.Status).SingleAsync();
    }

    public static async Task<int> ReversalAuditCountAsync(ApiFactory factory, Guid transactionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.AuditEntries.CountAsync(a => a.TransactionId == transactionId && a.Action == AuditAction.TransactionReversed);
    }

    public static async Task<long> LedgerSumAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Postings.SumAsync(p => p.AmountCents);
    }

    public static async Task<long> PostingSumAsync(ApiFactory factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Postings.Where(p => p.AccountId == accountId).SumAsync(p => p.AmountCents);
    }

    private sealed record ReversalTransferId(Guid TransactionId);
}
