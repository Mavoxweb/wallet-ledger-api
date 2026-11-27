using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WalletLedger.Domain.Audit;
using WalletLedger.Domain.Ledger;
using WalletLedger.Infrastructure.Persistence;
using static WalletLedger.IntegrationTests.Backoffice.ReversalFixtures;
using static WalletLedger.IntegrationTests.Transfers.TransferFixtures;

namespace WalletLedger.IntegrationTests.Backoffice;

[Collection("api")]
public sealed class ReversalTests(ApiFactory factory)
{
    private sealed record Problem(string? Code);

    private sealed record StatementItem(Guid TransactionId, string Type, string Status, bool Reversible);

    private sealed record StatementPage(StatementItem[] Items, string? NextCursor);

    [Fact]
    public async Task Admin_estorna_pix_e_saldos_voltam()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 5_000);
        var admin = await StaffUsers.AdminAsync(factory);

        var response = await ReverseAsync(admin, pixId);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var result = (await response.Content.ReadFromJsonAsync<ReversalResult>())!;
        result.OriginalTransactionId.ShouldBe(pixId);
        result.AmountCents.ShouldBe(5_000);
        (await BalanceAsync(factory, a.User.AccountId)).ShouldBe(10_000);
        (await BalanceAsync(factory, b.User.AccountId)).ShouldBe(100);
        (await StatusAsync(factory, pixId)).ShouldBe(TransactionStatus.Reversed);

        var statement = (await admin.GetFromJsonAsync<StatementPage>($"/api/v1/backoffice/accounts/{a.User.AccountId}/statement"))!;
        statement.Items.ShouldContain(i => i.TransactionId == result.ReversalTransactionId && i.Type == "reversal" && !i.Reversible);
        var original = statement.Items.Single(i => i.TransactionId == pixId);
        original.Status.ShouldBe("reversed");
        original.Reversible.ShouldBeFalse();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var entry = await db.AuditEntries.AsNoTracking().SingleAsync(e => e.TransactionId == pixId);
        entry.Action.ShouldBe(AuditAction.TransactionReversed);
        entry.Reason.ShouldBe(Reason);
        JsonDocument.Parse(entry.Details).RootElement.GetProperty("amountCents").GetInt64().ShouldBe(5_000);
    }

    [Fact]
    public async Task Operador_recebe_403_no_estorno()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 5_000);
        var operador = await StaffUsers.OperatorAsync(factory);

        var response = await ReverseAsync(operador, pixId);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StatusAsync(factory, pixId)).ShouldBe(TransactionStatus.Completed);
    }

    [Fact]
    public async Task Cliente_recebe_403_no_estorno()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 5_000);

        var response = await ReverseAsync(a.Client, pixId);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Estorno_sem_saldo_do_destino_da_422_e_nao_grava_nada()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var c = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 5_000);
        await PixAsync(b.Client, c.EvpKey, 5_000);
        var admin = await StaffUsers.AdminAsync(factory);

        var response = await ReverseAsync(admin, pixId);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<Problem>())!.Code.ShouldBe("insufficient_funds");
        (await StatusAsync(factory, pixId)).ShouldBe(TransactionStatus.Completed);
        (await ReversalAuditCountAsync(factory, pixId)).ShouldBe(0);
        (await BalanceAsync(factory, a.User.AccountId)).ShouldBe(5_000);
        (await BalanceAsync(factory, b.User.AccountId)).ShouldBe(100);
    }

    [Fact]
    public async Task Estorno_de_deposito_credita_liquidacao()
    {
        var customer = await FundedCustomerAsync(factory, 10_000);
        Guid depositId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
            depositId = await db.Postings
                .Where(p => p.AccountId == customer.User.AccountId)
                .Join(db.Transactions.Where(t => t.Type == TransactionType.Deposit), p => p.TransactionId, t => t.Id, (_, t) => t.Id)
                .SingleAsync();
        }

        var admin = await StaffUsers.AdminAsync(factory);

        var response = await ReverseAsync(admin, depositId);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await BalanceAsync(factory, customer.User.AccountId)).ShouldBe(0);
        (await LedgerSumAsync(factory)).ShouldBe(0);
    }

    [Fact]
    public async Task Estorno_ignora_bloqueio()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 5_000);
        var admin = await StaffUsers.AdminAsync(factory);
        (await admin.PostAsJsonAsync($"/api/v1/backoffice/accounts/{b.User.AccountId}/block", new { reason = "Conta usada para golpe." }))
            .EnsureSuccessStatusCode();

        var response = await ReverseAsync(admin, pixId);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await BalanceAsync(factory, a.User.AccountId)).ShouldBe(10_000);
        (await BalanceAsync(factory, b.User.AccountId)).ShouldBe(100);
    }

    [Fact]
    public async Task Mesma_chave_devolve_mesma_resposta()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 5_000);
        var admin = await StaffUsers.AdminAsync(factory);
        var key = Guid.NewGuid().ToString();

        var first = await ReverseAsync(admin, pixId, key);
        var second = await ReverseAsync(admin, pixId, key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await second.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await BalanceAsync(factory, a.User.AccountId)).ShouldBe(10_000);
        (await ReversalAuditCountAsync(factory, pixId)).ShouldBe(1);
    }

    [Fact]
    public async Task Estornar_de_novo_com_outra_chave_da_409()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 5_000);
        var admin = await StaffUsers.AdminAsync(factory);
        (await ReverseAsync(admin, pixId)).EnsureSuccessStatusCode();

        var again = await ReverseAsync(admin, pixId);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<Problem>())!.Code.ShouldBe("transaction_already_reversed");
    }

    [Fact]
    public async Task Estornar_um_estorno_da_422()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 5_000);
        var admin = await StaffUsers.AdminAsync(factory);
        var reversal = (await (await ReverseAsync(admin, pixId)).Content.ReadFromJsonAsync<ReversalResult>())!;

        var response = await ReverseAsync(admin, reversal.ReversalTransactionId);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<Problem>())!.Code.ShouldBe("reversal_not_allowed");
    }

    [Fact]
    public async Task Motivo_curto_da_400_e_nada_muda()
    {
        var a = await FundedCustomerAsync(factory, 10_000);
        var b = await FundedCustomerAsync(factory, 100);
        var pixId = await PixAsync(a.Client, b.EvpKey, 5_000);
        var admin = await StaffUsers.AdminAsync(factory);

        var response = await ReverseAsync(admin, pixId, reason: "engano");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StatusAsync(factory, pixId)).ShouldBe(TransactionStatus.Completed);
    }

    [Fact]
    public async Task Transacao_inexistente_da_404()
    {
        var admin = await StaffUsers.AdminAsync(factory);

        var response = await ReverseAsync(admin, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
