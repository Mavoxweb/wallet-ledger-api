using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WalletLedger.Application.Abstractions;

namespace WalletLedger.Api.Filters;

/// <summary>
/// Exige o header <c>Idempotency-Key</c> e calcula o hash do pedido (método + rota +
/// corpo). Roda antes da leitura do corpo pelo model binding, por isso consegue lê-lo
/// e devolvê-lo ao início para o resto do pipeline.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed partial class RequiresIdempotencyKeyAttribute : Attribute, IAsyncResourceFilter
{
    public const string HeaderName = "Idempotency-Key";
    private const string ItemKey = "idempotency";
    private const int MaxKeyLength = 100;

    public static IdempotencyContext From(HttpContext context) =>
        context.Items[ItemKey] as IdempotencyContext
        ?? throw new InvalidOperationException("Endpoint sem [RequiresIdempotencyKey].");

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString();

        if (key.Length is 0 or > MaxKeyLength || !KeyPattern().IsMatch(key))
        {
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = $"Header {HeaderName} obrigatório: de 1 a {MaxKeyLength} caracteres entre letras, números, '-' e '_'.",
            });
            return;
        }

        http.Request.EnableBuffering();
        using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(http.RequestAborted);
        http.Request.Body.Position = 0;

        var payload = string.Create(CultureInfo.InvariantCulture, $"{http.Request.Method} {http.Request.Path}\n{body}");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        http.Items[ItemKey] = new IdempotencyContext(key, hash);
        await next();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex KeyPattern();
}
