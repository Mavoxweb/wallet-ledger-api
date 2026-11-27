using WalletLedger.Domain.Users;

namespace WalletLedger.Domain.Audit;

public enum AuditAction
{
    AccountBlocked,
    AccountUnblocked,
    TransactionReversed,
}

/// <summary>
/// Registro de uma ação da operação: quem fez, com que perfil, em qual conta, por
/// quê e quando. Só é criado, nunca alterado, e é gravado na mesma transação da
/// ação: não existe ação sem registro nem registro sem ação.
/// </summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid ActorUserId { get; private set; }

    public Role ActorRole { get; private set; }

    public AuditAction Action { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid? TransactionId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    /// <summary>Contexto da ação em JSON (por exemplo, status anterior e novo).</summary>
    public string Details { get; private set; } = "{}";

    public DateTimeOffset CreatedAt { get; private set; }

    public static AuditEntry Create(
        Guid actorUserId,
        Role actorRole,
        AuditAction action,
        Guid accountId,
        Guid? transactionId,
        string reason,
        string detailsJson,
        DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailsJson);

        return new AuditEntry
        {
            Id = Guid.CreateVersion7(at),
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            Action = action,
            AccountId = accountId,
            TransactionId = transactionId,
            Reason = reason,
            Details = detailsJson,
            CreatedAt = at,
        };
    }
}
