using System.Net;
using System.Net.Http.Json;
using Shouldly;
using static WalletLedger.IntegrationTests.Backoffice.ReversalFixtures;
using static WalletLedger.IntegrationTests.Transfers.TransferFixtures;

namespace WalletLedger.IntegrationTests.Backoffice;

[Collection("api")]
public sealed class ReversalConcurrencyTests(ApiFactory factory)
{
    private sealed record Problem(string? Code);

    /// <summary>
    /// Dois admins clicam "Estornar" na mesma transação ao mesmo tempo, cada um com a
    /// sua chave de idempotência. A trava na linha da transação enfileira os dois: um
    /// estorna e o outro encontra a transação já estornada.
    /// </summary>
    [Fact]
    public async Task Dois_estornos_simultaneos_da_mesma_transacao()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 5_000);
        var first = await StaffUsers.AdminAsync(factory);
        var second = await StaffUsers.AdminAsync(factory);

        var responses = await Task.WhenAll(ReverseAsync(first, pixId), ReverseAsync(second, pixId));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        (await conflict.Content.ReadFromJsonAsync<Problem>())!.Code.ShouldBe("transaction_already_reversed");

        (await ReversalAuditCountAsync(factory, pixId)).ShouldBe(1);
        (await LedgerSumAsync(factory)).ShouldBe(0);
        foreach (var accountId in new[] { a.User.AccountId, b.User.AccountId })
        {
            (await BalanceAsync(factory, accountId)).ShouldBe(await PostingSumAsync(factory, accountId));
        }

        (await BalanceAsync(factory, a.User.AccountId)).ShouldBe(10_000);
    }
}
