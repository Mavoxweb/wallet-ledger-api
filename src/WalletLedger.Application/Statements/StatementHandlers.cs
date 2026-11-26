using System.Globalization;
using System.Text;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Ledger;
using WalletLedger.Domain.Users;

namespace WalletLedger.Application.Statements;

public sealed record AccountResponse(Guid AccountId, AccountStatus Status, long BalanceCents);

public enum Direction
{
    In,
    Out,
}

public sealed record StatementItem(
    long PostingId,
    Guid TransactionId,
    TransactionType Type,
    Direction Direction,
    long AmountCents,
    string? CounterpartyMaskedName,
    string? Description,
    DateTimeOffset CreatedAt);

public sealed record StatementPage(IReadOnlyList<StatementItem> Items, string? NextCursor);

public sealed class GetBalanceHandler(IWalletDbContext db)
{
    public async Task<AccountResponse> HandleAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new AccountResponse(a.Id, a.Status, a.BalanceCents))
            .SingleAsync(cancellationToken);
}

/// <summary>
/// Extrato com paginação por cursor. O cursor é o id do último lançamento visto
/// (sequencial, gerado pelo banco), e cada página pede "lançamentos com id menor
/// que o cursor". Lançamentos novos entram no topo e não deslocam as páginas
/// seguintes, ao contrário de OFFSET.
/// </summary>
public sealed class GetStatementHandler(IWalletDbContext db)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public async Task<StatementPage> HandleAsync(Guid accountId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        var before = Validate(limit, cursor);

        // Pede um a mais só para saber se existe próxima página.
        var postings = await db.Postings
            .AsNoTracking()
            .Where(p => p.AccountId == accountId && (before == null || p.Id < before))
            .OrderByDescending(p => p.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = postings.Count > limit;
        var page = postings.Take(limit).ToList();
        if (page.Count == 0)
        {
            return new StatementPage([], null);
        }

        var transactionIds = page.Select(p => p.TransactionId).Distinct().ToList();

        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(t => transactionIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        // Contraparte = dono da outra conta da transação (só existe em transferências entre clientes).
        var counterparties = await (
            from other in db.Postings
            where transactionIds.Contains(other.TransactionId) && other.AccountId != accountId
            join account in db.Accounts on other.AccountId equals account.Id
            join user in db.Users on account.OwnerUserId equals user.Id
            select new { other.TransactionId, user.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var counterpartyByTransaction = counterparties
            .GroupBy(c => c.TransactionId)
            .ToDictionary(g => g.Key, g => User.MaskName(g.First().Name));

        var items = page.Select(p =>
        {
            var transaction = transactions[p.TransactionId];
            return new StatementItem(
                p.Id,
                p.TransactionId,
                transaction.Type,
                p.AmountCents > 0 ? Direction.In : Direction.Out,
                Math.Abs(p.AmountCents),
                counterpartyByTransaction.GetValueOrDefault(p.TransactionId),
                transaction.Description,
                p.CreatedAt);
        }).ToList();

        return new StatementPage(items, hasMore ? EncodeCursor(page[^1].Id) : null);
    }

    private static long? Validate(int limit, string? cursor)
    {
        var errors = new List<ValidationFailure>();
        if (limit is < 1 or > MaxLimit)
        {
            errors.Add(new ValidationFailure("limit", $"O limite precisa estar entre 1 e {MaxLimit}."));
        }

        long? before = null;
        if (cursor is not null)
        {
            before = DecodeCursor(cursor);
            if (before is null)
            {
                errors.Add(new ValidationFailure("cursor", "Cursor inválido."));
            }
        }

        return errors.Count > 0 ? throw new ValidationException(errors) : before;
    }

    // O cursor é opaco para o cliente: o id em base64url. Ele só o devolve na próxima chamada.
    public static string EncodeCursor(long postingId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(postingId.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static long? DecodeCursor(string cursor)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
