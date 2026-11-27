using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Audit;
using WalletLedger.Domain.Users;
using WalletLedger.Infrastructure.Persistence;
using static WalletLedger.IntegrationTests.Transfers.TransferFixtures;

namespace WalletLedger.IntegrationTests.Backoffice;

[Collection("api")]
public sealed class AccountStatusTests(ApiFactory factory)
{
    private const string Reason = "Suspeita de fraude informada pelo cliente.";

    private sealed record Detail(Guid AccountId, string Status);

    private sealed record Problem(string? Code);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid accountId, string action, string? reason) =>
        client.PostAsJsonAsync($"/api/v1/backoffice/accounts/{accountId}/{action}", new { reason });

    private async Task<List<AuditEntry>> AuditAsync(Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.AuditEntries.AsNoTracking().Where(a => a.AccountId == accountId).OrderBy(a => a.CreatedAt).ToListAsync();
    }

    private async Task<Guid> UserIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    private async Task<AccountStatus> StatusAsync(Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Accounts.Where(a => a.Id == accountId).Select(a => a.Status).SingleAsync();
    }

    [Fact]
    public async Task Operador_bloqueia_com_motivo_e_auditoria_registra()
    {
        var customer = await TestUsers.RegisterAndLoginAsync(factory);
        var client = await StaffUsers.OperatorAsync(factory);

        var response = await PostAsync(client, customer.AccountId, "block", $"  {Reason}  ");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<Detail>())!.Status.ShouldBe("blocked");

        var entry = (await AuditAsync(customer.AccountId)).ShouldHaveSingleItem();
        entry.ActorUserId.ShouldBe(await UserIdAsync(StaffUsers.OperatorEmail));
        entry.ActorRole.ShouldBe(Role.Operator);
        entry.Action.ShouldBe(AuditAction.AccountBlocked);
        entry.Reason.ShouldBe(Reason);
        entry.TransactionId.ShouldBeNull();
    }

    [Fact]
    public async Task Motivo_curto_da_400()
    {
        var customer = await TestUsers.RegisterAndLoginAsync(factory);
        var client = await StaffUsers.OperatorAsync(factory);

        var response = await PostAsync(client, customer.AccountId, "block", "ok");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StatusAsync(customer.AccountId)).ShouldBe(AccountStatus.Active);
        (await AuditAsync(customer.AccountId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Motivo_so_com_espacos_da_400()
    {
        var customer = await TestUsers.RegisterAndLoginAsync(factory);
        var client = await StaffUsers.OperatorAsync(factory);

        var response = await PostAsync(client, customer.AccountId, "block", new string(' ', 20));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StatusAsync(customer.AccountId)).ShouldBe(AccountStatus.Active);
    }

    [Fact]
    public async Task Bloquear_duas_vezes_da_409_account_already_blocked()
    {
        var customer = await TestUsers.RegisterAndLoginAsync(factory);
        var client = await StaffUsers.OperatorAsync(factory);
        (await PostAsync(client, customer.AccountId, "block", Reason)).EnsureSuccessStatusCode();

        var again = await PostAsync(client, customer.AccountId, "block", Reason);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<Problem>())!.Code.ShouldBe("account_already_blocked");
        (await AuditAsync(customer.AccountId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cliente_bloqueado_nao_faz_pix_com_a_sessao_que_ja_tinha()
    {
        var payer = await FundedCustomerAsync(factory, 10_000);
        var payee = await FundedCustomerAsync(factory, 100);
        var client = await StaffUsers.OperatorAsync(factory);
        (await PostAsync(client, payer.User.AccountId, "block", Reason)).EnsureSuccessStatusCode();

        // Mesmo token de antes do bloqueio: a regra está na conta, não no token.
        var pix = await TransferAsync(payer.Client, "evp", payee.EvpKey, 500);

        pix.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await pix.Content.ReadFromJsonAsync<Problem>())!.Code.ShouldBe("account_not_active");
        (await BalanceAsync(factory, payer.User.AccountId)).ShouldBe(10_000);
    }

    [Fact]
    public async Task Desbloquear_reativa_e_audita()
    {
        var customer = await TestUsers.RegisterAndLoginAsync(factory);
        var client = await StaffUsers.AdminAsync(factory);
        (await PostAsync(client, customer.AccountId, "block", Reason)).EnsureSuccessStatusCode();

        var response = await PostAsync(client, customer.AccountId, "unblock", "Cliente confirmou a identidade por telefone.");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<Detail>())!.Status.ShouldBe("active");
        var entries = await AuditAsync(customer.AccountId);
        entries.Select(e => e.Action).ShouldBe([AuditAction.AccountBlocked, AuditAction.AccountUnblocked]);
        entries[1].ActorRole.ShouldBe(Role.Admin);
    }

    [Fact]
    public async Task Desbloquear_conta_ativa_da_409_account_not_blocked()
    {
        var customer = await TestUsers.RegisterAndLoginAsync(factory);
        var client = await StaffUsers.OperatorAsync(factory);

        var response = await PostAsync(client, customer.AccountId, "unblock", Reason);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<Problem>())!.Code.ShouldBe("account_not_blocked");
    }

    [Fact]
    public async Task Cliente_nao_bloqueia_conta_403()
    {
        var customer = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(customer.Tokens.AccessToken);

        var response = await PostAsync(client, customer.AccountId, "block", Reason);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StatusAsync(customer.AccountId)).ShouldBe(AccountStatus.Active);
    }

    [Fact]
    public async Task Bloquear_conta_inexistente_da_404()
    {
        var client = await StaffUsers.OperatorAsync(factory);

        var response = await PostAsync(client, Guid.NewGuid(), "block", Reason);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
