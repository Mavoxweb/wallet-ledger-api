using System.Globalization;
using System.Text;

namespace WalletLedger.Application.Backoffice;

/// <summary>
/// Cursor composto (instante + id) das listas do backoffice. Vários registros podem
/// ter o mesmo instante; o id desempata, e a página seguinte não pula nem repete.
/// Opaco para o cliente: "ticks|id" em base64url.
/// </summary>
public static class TimeIdCursor
{
    public static string Encode(DateTimeOffset at, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{at.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{id:N}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static (DateTimeOffset At, Guid Id)? Decode(string cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|');
            return parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && Guid.TryParseExact(parts[1], "N", out var id)
                ? (new DateTimeOffset(ticks, TimeSpan.Zero), id)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
