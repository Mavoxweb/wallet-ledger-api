namespace WalletLedger.Application.Common;

/// <summary>Horário de negócio do sistema: Brasília (America/Sao_Paulo).</summary>
public static class BusinessTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>Início e fim (em UTC) do dia do calendário de Brasília que contém <paramref name="instant"/>.</summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) DayOf(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Zone);
        var start = new DateTimeOffset(local.Date, Zone.GetUtcOffset(local.Date));
        var end = new DateTimeOffset(local.Date.AddDays(1), Zone.GetUtcOffset(local.Date.AddDays(1)));
        return (start.ToUniversalTime(), end.ToUniversalTime());
    }
}
