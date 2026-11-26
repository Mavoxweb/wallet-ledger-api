namespace WalletLedger.Domain.Users;

/// <summary>
/// CPF validado pelos dígitos verificadores (sem consulta à Receita).
/// Guarda apenas os 11 dígitos, sem máscara.
/// </summary>
public sealed record Cpf
{
    private Cpf(string digits) => Digits = digits;

    public string Digits { get; }

    /// <summary>Formato exibido para terceiros: <c>***.982.247-**</c>.</summary>
    public string Masked => $"***.{Digits[3..6]}.{Digits[6..9]}-**";

    public static bool TryParse(string? input, out Cpf? cpf)
    {
        cpf = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());

        // Sequências como 111.111.111-11 passam na conta dos dígitos, mas são inválidas.
        if (digits.Length != 11 || digits.Distinct().Count() == 1)
        {
            return false;
        }

        if (CheckDigit(digits, 9) != digits[9] - '0' || CheckDigit(digits, 10) != digits[10] - '0')
        {
            return false;
        }

        cpf = new Cpf(digits);
        return true;
    }

    /// <summary>Restore a partir do banco, onde o valor já foi validado.</summary>
    public static Cpf FromTrustedDigits(string digits) => new(digits);

    // Dígito verificador: soma ponderada dos dígitos anteriores (pesos decrescentes
    // a partir de length + 1), resto da divisão por 11; resto menor que 2 vira 0.
    private static int CheckDigit(string digits, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
        {
            sum += (digits[i] - '0') * (length + 1 - i);
        }

        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }
}
