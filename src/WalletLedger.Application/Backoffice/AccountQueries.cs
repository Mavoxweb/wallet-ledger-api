using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Application.Pix;
using WalletLedger.Domain.Accounts;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Users;

namespace WalletLedger.Application.Backoffice;

public sealed record BackofficeAccountSummary(
    Guid AccountId,
    string Number,
    string HolderName,
    string HolderEmail,
    AccountStatus Status,
    long BalanceCents,
    DateTimeOffset OpenedAt);

public sealed record BackofficeAccountPage(IReadOnlyList<BackofficeAccountSummary> Items, string? NextCursor);

public sealed record BackofficeAccountDetail(
    Guid AccountId,
    string Number,
    string HolderName,
    string HolderEmail,
    string HolderCpfMasked,
    AccountStatus Status,
    long BalanceCents,
    DateTimeOffset OpenedAt,
    IReadOnlyList<PixKeyResponse> PixKeys);

public static class AccountNumber
{
    /// <summary>
    /// O "número" que a pessoa vê: os 4 últimos caracteres do id, o mesmo
    /// <c>•••• 3822</c> que o app mostra.
    /// </summary>
    public static string Of(Guid accountId) => accountId.ToString("N")[^4..];
}

/// <summary>
/// Busca de contas de cliente para a operação. Um campo só: a busca descobre se o
/// texto é CPF, e-mail, final da conta ou nome. Paginação por cursor (data de
/// abertura + id), da conta mais nova para a mais antiga.
/// </summary>
public sealed class SearchAccountsHandler(IWalletDbContext db)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public async Task<BackofficeAccountPage> HandleAsync(
        string? q,
        AccountStatus? status,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var after = Validate(limit, cursor);

        var query =
            from account in db.Accounts.AsNoTracking()
            join user in db.Users.AsNoTracking() on account.OwnerUserId equals user.Id
            select new { account, user };

        if (status is not null)
        {
            query = query.Where(x => x.account.Status == status);
        }

        var text = q?.Trim() ?? string.Empty;
        if (text.Length > 0)
        {
            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            if (text.Contains('@', StringComparison.Ordinal))
            {
                var email = User.NormalizeEmail(text);
                query = query.Where(x => x.user.Email == email);
            }
            else if (digits.Length == 11 && text.All(c => char.IsAsciiDigit(c) || c is '.' or '-' or ' '))
            {
                var cpf = Cpf.FromTrustedDigits(digits);
                query = query.Where(x => x.user.Cpf == cpf);
            }
            else
            {
                // Nome em qualquer parte; 4 caracteres hexadecimais também podem ser
                // o final da conta (o "•••• 3822" que o cliente vê no app).
                var lowered = text.ToLowerInvariant();
                var pattern = $"%{EscapeLike(lowered)}%";
                var maybeNumber = lowered.Length == 4 && lowered.All(char.IsAsciiHexDigit);
                var numberPattern = $"%{lowered}";
                // ToLower() aqui vira lower() no SQL: a cultura do .NET não participa.
#pragma warning disable CA1304, CA1311
                query = query.Where(x =>
                    EF.Functions.Like(x.user.Name.ToLower(), pattern, "\\") ||
                    (maybeNumber && EF.Functions.Like(x.account.Id.ToString(), numberPattern)));
#pragma warning restore CA1304, CA1311
            }
        }

        if (after is { } c)
        {
            query = query.Where(x =>
                x.account.CreatedAt < c.OpenedAt ||
                (x.account.CreatedAt == c.OpenedAt && x.account.Id.CompareTo(c.Id) < 0));
        }

        var rows = await query
            .OrderByDescending(x => x.account.CreatedAt)
            .ThenByDescending(x => x.account.Id)
            .Take(limit + 1)
            .Select(x => new BackofficeAccountSummary(
                x.account.Id,
                string.Empty,
                x.user.Name,
                x.user.Email,
                x.account.Status,
                x.account.BalanceCents,
                x.account.CreatedAt))
            .ToListAsync(cancellationToken);

        var page = rows.Take(limit).Select(r => r with { Number = AccountNumber.Of(r.AccountId) }).ToList();
        var next = rows.Count > limit ? TimeIdCursor.Encode(page[^1].OpenedAt, page[^1].AccountId) : null;
        return new BackofficeAccountPage(page, next);
    }

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record Cursor(DateTimeOffset OpenedAt, Guid Id);

    private static Cursor? Validate(int limit, string? cursor)
    {
        var errors = new List<ValidationFailure>();
        if (limit is < 1 or > MaxLimit)
        {
            errors.Add(new ValidationFailure("limit", $"O limite precisa estar entre 1 e {MaxLimit}."));
        }

        Cursor? after = null;
        if (cursor is not null)
        {
            after = DecodeCursor(cursor);
            if (after is null)
            {
                errors.Add(new ValidationFailure("cursor", "Cursor inválido."));
            }
        }

        return errors.Count > 0 ? throw new ValidationException(errors) : after;
    }

    private static Cursor? DecodeCursor(string cursor) =>
        TimeIdCursor.Decode(cursor) is { } c ? new Cursor(c.At, c.Id) : null;
}

public sealed class GetAccountDetailHandler(IWalletDbContext db)
{
    public async Task<BackofficeAccountDetail> HandleAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var row = await (
            from account in db.Accounts.AsNoTracking()
            join user in db.Users.AsNoTracking() on account.OwnerUserId equals user.Id
            where account.Id == accountId
            select new { account, user })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("not_found", "Conta não encontrada.");

        var keys = await db.PixKeys
            .AsNoTracking()
            .Where(k => k.AccountId == accountId)
            .OrderBy(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

        return new BackofficeAccountDetail(
            row.account.Id,
            AccountNumber.Of(row.account.Id),
            row.user.Name,
            row.user.Email,
            row.user.Cpf.Masked,
            row.account.Status,
            row.account.BalanceCents,
            row.account.CreatedAt,
            keys.Select(PixKeyResponse.From).ToList());
    }
}
