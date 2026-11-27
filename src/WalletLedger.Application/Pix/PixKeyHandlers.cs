using Microsoft.EntityFrameworkCore;
using WalletLedger.Application.Abstractions;
using WalletLedger.Domain.Common;
using WalletLedger.Domain.Pix;

namespace WalletLedger.Application.Pix;

public sealed record CreatePixKeyRequest(PixKeyType Type, string? Value);

public sealed record PixKeyResponse(Guid Id, PixKeyType Type, string Value, DateTimeOffset CreatedAt)
{
    public static PixKeyResponse From(PixKey key) => new(key.Id, key.Type, key.Value, key.CreatedAt);
}

public sealed record PixKeyLookupResponse(PixKeyType KeyType, string MaskedName, string MaskedCpf);

public sealed class CreatePixKeyHandler(IWalletDbContext db, IAccountLocker locker, TimeProvider clock)
{
    public async Task<PixKeyResponse> HandleAsync(Guid accountId, CreatePixKeyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = PixKey.Create(accountId, request.Type, request.Value, clock.GetUtcNow());

        // Trava a conta para que dois cadastros simultâneos não passem juntos do limite de 5.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await locker.LockAsync([accountId], cancellationToken);

        var count = await db.PixKeys.CountAsync(k => k.AccountId == accountId, cancellationToken);
        if (count >= PixKey.MaxKeysPerAccount)
        {
            throw new DomainException("pix_key_limit", $"Cada conta pode ter até {PixKey.MaxKeysPerAccount} chaves PIX.");
        }

        if (await db.PixKeys.AnyAsync(k => k.Value == key.Value, cancellationToken))
        {
            throw new DomainException("pix_key_taken", "Esta chave PIX já está cadastrada.");
        }

        db.PixKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return PixKeyResponse.From(key);
    }
}

public sealed class ListPixKeysHandler(IWalletDbContext db)
{
    public async Task<IReadOnlyList<PixKeyResponse>> HandleAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var keys = await db.PixKeys
            .AsNoTracking()
            .Where(k => k.AccountId == accountId)
            .OrderBy(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

        return keys.Select(PixKeyResponse.From).ToList();
    }
}

public sealed class DeletePixKeyHandler(IWalletDbContext db)
{
    public async Task HandleAsync(Guid accountId, Guid keyId, CancellationToken cancellationToken)
    {
        // Chave de outra conta responde igual a chave inexistente: não revela que ela existe.
        var deleted = await db.PixKeys
            .Where(k => k.Id == keyId && k.AccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            throw new DomainException("not_found", "Chave PIX não encontrada.");
        }
    }
}

public sealed class LookupPixKeyHandler(IWalletDbContext db)
{
    /// <summary>
    /// Mostra a quem pertence uma chave antes do PIX, como nos bancos: só nome e CPF
    /// mascarados, nunca e-mail, CPF completo ou saldo.
    /// </summary>
    public async Task<PixKeyLookupResponse> HandleAsync(PixKeyType type, string value, CancellationToken cancellationToken)
    {
        var normalized = PixKey.Normalize(type, value);

        var owner = await (
            from key in db.PixKeys
            join account in db.Accounts on key.AccountId equals account.Id
            join user in db.Users on account.OwnerUserId equals user.Id
            where key.Value == normalized && key.Type == type
            select user).AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("not_found", "Chave PIX não encontrada.");

        return new PixKeyLookupResponse(type, owner.MaskedName, owner.Cpf.Masked);
    }
}
