namespace WalletLedger.Domain.Common;

/// <summary>
/// Registro de uma requisição idempotente: a chave enviada pelo cliente, um hash do
/// pedido e a resposta que foi devolvida. Uma repetição com a mesma chave recebe a
/// mesma resposta, sem executar a operação de novo.
/// </summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
    }

    public Guid UserId { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public int? StatusCode { get; private set; }

    public string? ResponseBody { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static IdempotencyRecord Start(Guid userId, string key, string requestHash, DateTimeOffset now) =>
        new() { UserId = userId, Key = key, RequestHash = requestHash, CreatedAt = now };

    public void Complete(int statusCode, string responseBody)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
