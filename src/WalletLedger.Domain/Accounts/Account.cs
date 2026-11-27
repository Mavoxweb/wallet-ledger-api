using WalletLedger.Domain.Common;

namespace WalletLedger.Domain.Accounts;

public enum AccountStatus
{
    Active,
    Blocked,
    Closed,
}

/// <summary>
/// Conta da carteira. <see cref="BalanceCents"/> é um cache do saldo: a fonte da
/// verdade é a soma dos lançamentos no razão, e a conciliação confere os dois.
/// O cache só é alterado com a conta travada no banco (ver <c>IAccountLocker</c>).
/// </summary>
public sealed class Account
{
    /// <summary>Contrapartida dos depósitos: representa o dinheiro que entra de fora.</summary>
    public const string SettlementAccountCode = "liquidacao-externa";

    private Account()
    {
    }

    public Guid Id { get; private set; }

    public Guid? OwnerUserId { get; private set; }

    public string? SystemCode { get; private set; }

    public AccountStatus Status { get; private set; }

    public long BalanceCents { get; private set; }

    /// <summary>Só contas de sistema podem ficar negativas.</summary>
    public bool AllowsNegative { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Account OpenForCustomer(Guid userId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            OwnerUserId = userId,
            Status = AccountStatus.Active,
            CreatedAt = now,
        };

    public static Account CreateSystem(string code, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            SystemCode = code,
            Status = AccountStatus.Active,
            AllowsNegative = true,
            CreatedAt = now,
        };

    public void Debit(Money amount)
    {
        EnsurePositive(amount);
        EnsureActive();

        var newBalance = checked(BalanceCents - amount.Cents);
        if (newBalance < 0 && !AllowsNegative)
        {
            throw new DomainException("insufficient_funds", "Saldo insuficiente.");
        }

        BalanceCents = newBalance;
    }

    public void Credit(Money amount)
    {
        EnsurePositive(amount);
        EnsureActive();
        BalanceCents = checked(BalanceCents + amount.Cents);
    }

    public void EnsureActive()
    {
        if (Status != AccountStatus.Active)
        {
            throw new DomainException("account_not_active", "A conta não está ativa.");
        }
    }

    /// <summary>
    /// Bloqueio pela operação. Não mexe no saldo: a conta só para de movimentar,
    /// porque <see cref="Debit"/> e <see cref="Credit"/> exigem conta ativa.
    /// </summary>
    public void Block()
    {
        if (Status == AccountStatus.Blocked)
        {
            throw new DomainException("account_already_blocked", "A conta já está bloqueada.");
        }

        EnsureActive();
        Status = AccountStatus.Blocked;
    }

    public void Unblock()
    {
        if (Status != AccountStatus.Blocked)
        {
            throw new DomainException("account_not_blocked", "A conta não está bloqueada.");
        }

        Status = AccountStatus.Active;
    }

    private static void EnsurePositive(Money amount)
    {
        if (!amount.IsPositive)
        {
            throw new DomainException("invalid_amount", "O valor precisa ser maior que zero.");
        }
    }
}
