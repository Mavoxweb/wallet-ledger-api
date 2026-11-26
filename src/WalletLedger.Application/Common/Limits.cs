namespace WalletLedger.Application.Common;

public static class Limits
{
    /// <summary>
    /// Maior valor aceito em qualquer operação (R$ 100 milhões). Valores acima disso são
    /// erro de entrada, não regra de negócio, e protegem contra estouro de soma.
    /// </summary>
    public const long MaxAmountCents = 10_000_000_000;
}
