using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Application.Statements;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Ledger;

namespace WalletLedger.Application.Backoffice;

public sealed record BackofficeStatementItem(
    long PostingId,
    Guid TransactionId,
    TransactionType Type,
    TransactionStatus Status,
    Direction Direction,
    long AmountCents,
    string? CounterpartyMaskedName,
    string? Description,
    DateTimeOffset CreatedAt,
    bool Reversible);

public sealed record BackofficeStatementPage(IReadOnlyList<BackofficeStatementItem> Items, string? NextCursor);

/// <summary>
/// Extrato de qualquer conta de cliente para a operação. Reaproveita o extrato do
/// cliente (mesma paginação, mesmo cursor) e acrescenta o status da transação e se
/// ela ainda pode ser estornada. O extrato do app não muda.
/// </summary>
public sealed class BackofficeStatementHandler(IWalletDbContext db, GetStatementHandler statement)
{
    public async Task<BackofficeStatementPage> HandleAsync(
        Guid accountId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var exists = await db.Accounts.AnyAsync(a => a.Id == accountId && a.OwnerUserId != null, cancellationToken);
        if (!exists)
        {
            throw new DomainException("not_found", "Conta não encontrada.");
        }

        var page = await statement.HandleAsync(accountId, limit, cursor, cancellationToken);

        var transactionIds = page.Items.Select(i => i.TransactionId).Distinct().ToList();
        var statusById = await db.Transactions
            .AsNoTracking()
            .Where(t => transactionIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Status, cancellationToken);

        var items = page.Items.Select(i =>
        {
            var status = statusById[i.TransactionId];
            return new BackofficeStatementItem(
                i.PostingId,
                i.TransactionId,
                i.Type,
                status,
                i.Direction,
                i.AmountCents,
                i.CounterpartyMaskedName,
                i.Description,
                i.CreatedAt,
                Reversible: status == TransactionStatus.Completed && i.Type != TransactionType.Reversal);
        }).ToList();

        return new BackofficeStatementPage(items, page.NextCursor);
    }
}
