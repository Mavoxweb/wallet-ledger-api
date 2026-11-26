namespace WalletLedger.Domain.Common;

/// <summary>
/// Violação de uma regra de negócio. O <see cref="Code"/> é estável e vai para o
/// cliente da API (por exemplo, <c>insufficient_funds</c>), enquanto a mensagem é
/// só para leitura humana.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message) => Code = code;

    public DomainException()
        : this("domain_error", "Regra de negócio violada.")
    {
    }

    public DomainException(string message)
        : this("domain_error", message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException) => Code = "domain_error";

    public string Code { get; }
}
