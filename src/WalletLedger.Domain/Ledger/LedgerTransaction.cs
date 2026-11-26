using WalletLedger.Domain.Common;

namespace WalletLedger.Domain.Ledger;

public enum TransactionType
{
    Deposit,
    Transfer,
    Reversal,
}

public enum TransactionStatus
{
    Completed,
    Reversed,
}

/// <summary>
/// Uma transação do razão de partidas dobradas: um conjunto de lançamentos que
/// somam exatamente zero. Todo centavo que sai de uma conta entra em outra, então a
/// soma de todos os lançamentos do sistema é sempre zero, e isso é auditável.
/// </summary>
public sealed class LedgerTransaction
{
    private readonly List<Posting> _postings = [];

    private LedgerTransaction()
    {
    }

    public Guid Id { get; private set; }

    public TransactionType Type { get; private set; }

    public TransactionStatus Status { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<Posting> Postings => _postings;

    public static LedgerTransaction Create(
        TransactionType type,
        string description,
        IReadOnlyList<(Guid AccountId, long AmountCents)> postings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(postings);

        if (postings.Count < 2)
        {
            throw new DomainException("unbalanced_transaction", "Uma transação precisa de pelo menos dois lançamentos.");
        }

        if (postings.Any(p => p.AmountCents == 0))
        {
            throw new DomainException("invalid_amount", "Lançamento com valor zero.");
        }

        if (postings.Sum(p => p.AmountCents) != 0)
        {
            throw new DomainException("unbalanced_transaction", "Os lançamentos precisam somar zero.");
        }

        var transaction = new LedgerTransaction
        {
            Id = Guid.CreateVersion7(now),
            Type = type,
            Status = TransactionStatus.Completed,
            Description = description,
            CreatedAt = now,
        };

        foreach (var (accountId, amountCents) in postings)
        {
            transaction._postings.Add(new Posting(transaction.Id, accountId, amountCents, now));
        }

        return transaction;
    }
}
