using System.Text.Json;
using System.Text.Json.Serialization;
using WalletLedger.Application.Abstractions;

namespace WalletLedger.Application.Common;

/// <summary>Serialização das respostas guardadas para idempotência, igual à da API (camelCase).</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static IdempotentResult Created(object body) => new(201, JsonSerializer.Serialize(body, Options));
}
