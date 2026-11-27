using System.Globalization;

namespace WalletLedger.Domain.Common;

/// <summary>
/// Valor monetário em centavos de real. Não existe construtor a partir de
/// <c>double</c> ou <c>decimal</c>: um centavo perdido num razão é um bug.
/// </summary>
public readonly record struct Money(long Cents)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static Money FromCents(long cents) => new(cents);

    public bool IsPositive => Cents > 0;

    public static Money operator +(Money left, Money right) => new(checked(left.Cents + right.Cents));

    public static Money operator -(Money left, Money right) => new(checked(left.Cents - right.Cents));

    public static Money Add(Money left, Money right) => left + right;

    public static Money Subtract(Money left, Money right) => left - right;

    // Divide por 100 só para exibir; o valor guardado continua inteiro.
    public override string ToString() =>
        string.Create(PtBr, $"R$ {Cents / 100m:N2}");
}
