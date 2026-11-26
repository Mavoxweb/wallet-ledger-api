using System.Net.Http.Json;
using Shouldly;

namespace WalletLedger.IntegrationTests;

public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);

public sealed record RegisterResponse(Guid UserId, Guid AccountId);

public sealed record TestUser(string Email, string Password, Guid UserId, Guid AccountId, TokenResponse Tokens);

/// <summary>Atalhos para criar clientes de teste pela própria API.</summary>
public static class TestUsers
{
    public const string Password = "Senha1234";

    private static int _cpfSeed = 100_000_000;

    /// <summary>Gera um CPF válido e diferente a cada chamada.</summary>
    public static string NextCpf()
    {
        var baseDigits = Interlocked.Increment(ref _cpfSeed).ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var d1 = CheckDigit(baseDigits);
        var d2 = CheckDigit(baseDigits + d1);
        return $"{baseDigits}{d1}{d2}";
    }

    public static string NewEmail() => $"{Guid.NewGuid():N}@teste.dev";

    public static async Task<TestUser> RegisterAndLoginAsync(ApiFactory factory, string name = "Cliente Teste")
    {
        var client = factory.CreateClient();
        var email = NewEmail();

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new { name, cpf = NextCpf(), email, password = Password });
        register.EnsureSuccessStatusCode();
        var registered = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!;

        var tokens = await LoginAsync(client, email, Password);
        return new TestUser(email, Password, registered.UserId, registered.AccountId, tokens);
    }

    public static async Task<TokenResponse> LoginAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        login.IsSuccessStatusCode.ShouldBeTrue($"login falhou: {(int)login.StatusCode}");
        return (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static int CheckDigit(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            sum += (digits[i] - '0') * (digits.Length + 1 - i);
        }

        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }
}
