using System.Globalization;

namespace WalletLedger.Domain.Common;

/// <summary>
/// Valor monetário em centavos de real. Dinheiro nunca é representado em ponto
/// flutuante: <c>0.1 + 0.2</c> não dá <c>0.3</c> em <c>double</c>, e um centavo
/// perdido num razão contábil é um bug. Por isso não existe construtor a partir de
/// <c>double</c> ou <c>decimal</c>.
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
