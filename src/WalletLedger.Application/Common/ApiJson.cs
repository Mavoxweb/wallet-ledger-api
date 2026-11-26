using System.Text.Json;
using WalletLedger.Application.Abstractions;

namespace WalletLedger.Application.Common;

/// <summary>Serialização das respostas guardadas para idempotência, igual à da API (camelCase).</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IdempotentResult Created(object body) => new(201, JsonSerializer.Serialize(body, Options));
}
