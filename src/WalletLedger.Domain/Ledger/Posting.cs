namespace WalletLedger.Domain.Ledger;

/// <summary>
/// Lançamento no razão: crédito (positivo) ou débito (negativo) numa conta.
/// É imutável: nada é corrigido por UPDATE ou DELETE, só por um estorno que gera
/// lançamentos opostos. O <see cref="Id"/> sequencial (gerado pelo banco) dá a ordem
/// estável usada no extrato.
/// </summary>
public sealed class Posting
{
    private Posting()
    {
    }

    internal Posting(Guid transactionId, Guid accountId, long amountCents, DateTimeOffset createdAt)
    {
        TransactionId = transactionId;
        AccountId = accountId;
        AmountCents = amountCents;
        CreatedAt = createdAt;
    }

    public long Id { get; private set; }

    public Guid TransactionId { get; private set; }

    public Guid AccountId { get; private set; }

    public long AmountCents { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
