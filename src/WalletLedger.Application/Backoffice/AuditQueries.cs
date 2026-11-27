using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Audit;
using WalletLedger.Domain.Users;

namespace WalletLedger.Application.Backoffice;

public sealed record AuditItem(
    Guid Id,
    DateTimeOffset At,
    Guid ActorId,
    string ActorName,
    Role ActorRole,
    AuditAction Action,
    Guid AccountId,
    string AccountNumber,
    Guid? TransactionId,
    string Reason,
    JsonElement Details);

public sealed record AuditPage(IReadOnlyList<AuditItem> Items, string? NextCursor);

/// <summary>
/// Trilha de auditoria, da ação mais recente para a mais antiga, com filtros por
/// conta, por quem fez e por tipo de ação. Paginação por cursor (data + id).
/// </summary>
public sealed class GetAuditHandler(IWalletDbContext db)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public async Task<AuditPage> HandleAsync(
        Guid? accountId,
        Guid? actorId,
        AuditAction? action,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var after = Validate(limit, cursor);

        var query =
            from entry in db.AuditEntries.AsNoTracking()
            join actor in db.Users.AsNoTracking() on entry.ActorUserId equals actor.Id
            select new { entry, actor.Name };

        if (accountId is not null)
        {
            query = query.Where(x => x.entry.AccountId == accountId);
        }

        if (actorId is not null)
        {
            query = query.Where(x => x.entry.ActorUserId == actorId);
        }

        if (action is not null)
        {
            query = query.Where(x => x.entry.Action == action);
        }

        if (after is { } c)
        {
            query = query.Where(x =>
                x.entry.CreatedAt < c.At ||
                (x.entry.CreatedAt == c.At && x.entry.Id.CompareTo(c.Id) < 0));
        }

        var rows = await query
            .OrderByDescending(x => x.entry.CreatedAt)
            .ThenByDescending(x => x.entry.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var page = rows.Take(limit).Select(x =>
        {
            using var details = JsonDocument.Parse(x.entry.Details);
            return new AuditItem(
                x.entry.Id,
                x.entry.CreatedAt,
                x.entry.ActorUserId,
                x.Name,
                x.entry.ActorRole,
                x.entry.Action,
                x.entry.AccountId,
                Backoffice.AccountNumber.Of(x.entry.AccountId),
                x.entry.TransactionId,
                x.entry.Reason,
                details.RootElement.Clone());
        }).ToList();

        var next = rows.Count > limit ? TimeIdCursor.Encode(page[^1].At, page[^1].Id) : null;
        return new AuditPage(page, next);
    }

    private sealed record Cursor(DateTimeOffset At, Guid Id);

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
