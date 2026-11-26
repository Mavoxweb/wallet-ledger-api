using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WalletLedger.Domain.Accounts;
using WalletLedger.Infrastructure.Persistence;

namespace WalletLedger.IntegrationTests.Auth;

[Collection("api")]
public sealed class AuthTests(ApiFactory factory)
{
    private sealed record MeResponse(Guid UserId, string Name, string Role, Guid? AccountId);

    [Fact]
    public async Task Register_creates_user_and_active_account()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory, "Maria Clara Souza");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var account = await db.Accounts.SingleAsync(a => a.Id == user.AccountId);

        account.OwnerUserId.ShouldBe(user.UserId);
        account.Status.ShouldBe(AccountStatus.Active);
        account.BalanceCents.ShouldBe(0);
    }

    [Fact]
    public async Task Register_with_invalid_cpf_returns_400_with_errors()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { name = "Fulano", cpf = "529.982.247-26", email = TestUsers.NewEmail(), password = TestUsers.Password });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldContain(n => n.Equals("cpf", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("curta1")]
    [InlineData("semnumeros")]
    [InlineData("12345678")]
    public async Task Register_with_weak_password_returns_400(string password)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { name = "Fulano", cpf = TestUsers.NextCpf(), email = TestUsers.NewEmail(), password });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_duplicate_email_returns_409()
    {
        var client = factory.CreateClient();
        var email = TestUsers.NewEmail();
        (await client.PostAsJsonAsync("/api/v1/auth/register",
            new { name = "Primeiro", cpf = TestUsers.NextCpf(), email, password = TestUsers.Password })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { name = "Segundo", cpf = TestUsers.NextCpf(), email = $"  {email.ToUpperInvariant()} ", password = TestUsers.Password });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_duplicate_cpf_returns_409()
    {
        var client = factory.CreateClient();
        var cpf = TestUsers.NextCpf();
        (await client.PostAsJsonAsync("/api/v1/auth/register",
            new { name = "Primeiro", cpf, email = TestUsers.NewEmail(), password = TestUsers.Password })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { name = "Segundo", cpf, email = TestUsers.NewEmail(), password = TestUsers.Password });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "Errada123" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_profile_from_token()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory, "Joana Prado");

        var me = await factory.CreateAuthenticatedClient(user.Tokens.AccessToken).GetFromJsonAsync<MeResponse>("/api/v1/me");

        me!.UserId.ShouldBe(user.UserId);
        me.Name.ShouldBe("Joana Prado");
        me.Role.ShouldBe("Customer");
        me.AccountId.ShouldBe(user.AccountId);
    }

    [Fact]
    public async Task Refresh_rotates_token_and_old_token_stops_working()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.Tokens.RefreshToken });
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = (await first.Content.ReadFromJsonAsync<TokenResponse>())!;
        rotated.RefreshToken.ShouldNotBe(user.Tokens.RefreshToken);

        var again = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.Tokens.RefreshToken });
        again.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reusing_revoked_refresh_token_revokes_all_user_tokens()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateClient();
        var rotated = (await (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.Tokens.RefreshToken }))
            .Content.ReadFromJsonAsync<TokenResponse>())!;

        // Alguém reaproveita o token antigo (possível roubo)...
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.Tokens.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ...e o token novo, legítimo, também deixa de valer: o usuário precisa logar de novo.
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = user.Tokens.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.Tokens.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_endpoint_with_invalid_token_returns_401()
    {
        var response = await factory.CreateAuthenticatedClient("nao.e.um.jwt").GetAsync("/api/v1/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_endpoint_with_expired_token_returns_401()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);

        // O access token vale 15 minutos.
        factory.Clock.Advance(TimeSpan.FromMinutes(16));

        var response = await factory.CreateAuthenticatedClient(user.Tokens.AccessToken).GetAsync("/api/v1/me");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Demo_seed_creates_customers_with_balance()
    {
        var tokens = await TestUsers.LoginAsync(factory.CreateClient(), "ana@walletledger.dev", "Demo@1234");
        var me = await factory.CreateAuthenticatedClient(tokens.AccessToken).GetFromJsonAsync<MeResponse>("/api/v1/me");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var account = await db.Accounts.SingleAsync(a => a.Id == me!.AccountId);
        account.BalanceCents.ShouldBe(250_000);

        var ledgerSum = await db.Postings.SumAsync(p => p.AmountCents);
        ledgerSum.ShouldBe(0);
    }
}
