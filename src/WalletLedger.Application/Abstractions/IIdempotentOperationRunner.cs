namespace WalletLedger.Application.Abstractions;

/// <summary>Resposta já serializada, pronta para ser guardada e devolvida igual numa repetição.</summary>
public sealed record IdempotentResult(int StatusCode, string JsonBody);

/// <summary>Dados de idempotência que a camada HTTP extrai da requisição.</summary>
public sealed record IdempotencyContext(string Key, string RequestHash);

public interface IIdempotentOperationRunner
{
    /// <summary>
    /// Executa <paramref name="operation"/> dentro de uma transação de banco em que a
    /// chave de idempotência é gravada primeiro. Se a chave já existir, devolve a
    /// resposta guardada sem executar nada. Tudo o que a operação gravar entra na
    /// mesma transação.
    /// </summary>
    Task<IdempotentResult> RunAsync(
        Guid userId,
        IdempotencyContext idempotency,
        Func<CancellationToken, Task<IdempotentResult>> operation,
        CancellationToken cancellationToken);
}
