using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WalletLedger.Domain.Common;
using WalletLedger.Infrastructure.Idempotency;
using WalletLedger.Infrastructure.Persistence;

namespace WalletLedger.IntegrationTests.Idempotency;

[Collection("api")]
public sealed class IdempotencyTests(ApiFactory factory)
{
    private sealed record DepositResponse(Guid TransactionId, long BalanceCents);

    private static Task<HttpResponseMessage> DepositAsync(HttpClient client, long amountCents, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/deposits/simulated")
        {
            Content = JsonContent.Create(new { amountCents }),
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private async Task<int> CountDepositsAsync(Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Postings.CountAsync(p => p.AccountId == accountId);
    }

    [Fact]
    public async Task Same_key_same_body_returns_identical_response_and_creates_one_transaction()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);
        var key = Guid.NewGuid().ToString();

        var first = await DepositAsync(client, 5_000, key);
        var second = await DepositAsync(client, 5_000, key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await second.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await CountDepositsAsync(user.AccountId)).ShouldBe(1);
    }

    [Fact]
    public async Task Same_key_different_body_returns_422()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);
        var key = Guid.NewGuid().ToString();
        (await DepositAsync(client, 5_000, key)).EnsureSuccessStatusCode();

        var response = await DepositAsync(client, 9_999, key);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("idempotency_key_reused");
        (await CountDepositsAsync(user.AccountId)).ShouldBe(1);
    }

    [Fact]
    public async Task Two_parallel_requests_same_key_create_exactly_one_transaction()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(DepositAsync(client, 7_000, key), DepositAsync(client, 7_000, key));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<DepositResponse>()));
        bodies[0]!.TransactionId.ShouldBe(bodies[1]!.TransactionId);
        (await CountDepositsAsync(user.AccountId)).ShouldBe(1);
    }

    [Fact]
    public async Task Same_key_from_different_users_does_not_collide()
    {
        var ana = await TestUsers.RegisterAndLoginAsync(factory);
        var bia = await TestUsers.RegisterAndLoginAsync(factory);
        var key = Guid.NewGuid().ToString();

        (await DepositAsync(factory.CreateAuthenticatedClient(ana.Tokens.AccessToken), 1_000, key)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await DepositAsync(factory.CreateAuthenticatedClient(bia.Tokens.AccessToken), 1_000, key)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CountDepositsAsync(bia.AccountId)).ShouldBe(1);
    }

    [Fact]
    public async Task Business_rule_failure_does_not_store_key()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);
        var key = Guid.NewGuid().ToString();

        (await DepositAsync(client, 100_001, key)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        (await db.IdempotencyKeys.AnyAsync(k => k.Key == key)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc def")]
    [InlineData("chave/invalida")]
    public async Task Invalid_key_returns_400(string key)
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);

        var response = await DepositAsync(factory.CreateAuthenticatedClient(user.Tokens.AccessToken), 100, key);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Key_longer_than_100_chars_returns_400()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);

        var response = await DepositAsync(factory.CreateAuthenticatedClient(user.Tokens.AccessToken), 100, new string('a', 101));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Missing_key_returns_400()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var client = factory.CreateAuthenticatedClient(user.Tokens.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/deposits/simulated", new { amountCents = 100 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cleanup_removes_keys_older_than_24h()
    {
        var user = await TestUsers.RegisterAndLoginAsync(factory);
        var now = factory.Clock.GetUtcNow();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var old = IdempotencyRecord.Start(user.UserId, $"antiga-{Guid.NewGuid():N}", "hash", now.AddHours(-25));
        var recent = IdempotencyRecord.Start(user.UserId, $"recente-{Guid.NewGuid():N}", "hash", now.AddHours(-23));
        old.Complete(201, "{}");
        recent.Complete(201, "{}");
        db.IdempotencyKeys.AddRange(old, recent);
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<IdempotencyCleanupService>().PurgeAsync(CancellationToken.None);

        (await db.IdempotencyKeys.AnyAsync(k => k.Key == old.Key)).ShouldBeFalse();
        (await db.IdempotencyKeys.AnyAsync(k => k.Key == recent.Key)).ShouldBeTrue();
    }
}
