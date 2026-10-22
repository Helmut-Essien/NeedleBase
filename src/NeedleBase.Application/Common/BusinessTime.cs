namespace NeedleBase.Application.Common;

/// <summary>
/// Business-local calendar for Ghana. Use for "today" (overdue badges, due-date defaults);
/// store instants as UTC. Falls back to UTC if the zone is missing on a device (Accra is UTC+0).
/// </summary>
public static class BusinessTime
{
    public const string TimeZoneId = "Africa/Accra";

    private static readonly TimeZoneInfo Zone = Resolve();

    public static DateOnly Today(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, Zone).DateTime);

    /// <summary>The instant in Accra time (for display: receipts, payment dates).</summary>
    public static DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
