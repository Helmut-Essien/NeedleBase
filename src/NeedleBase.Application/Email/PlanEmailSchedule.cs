using NeedleBase.Application.Common;

namespace NeedleBase.Application.Email;

/// <summary>
/// When paid-plan summary and due emails may go out (Ghana / Accra calendar).
/// Monthly from the 1st after 06:00; weekly from Monday after 06:00; due emails on the due day after 06:00.
/// Catch-up is allowed later the same period if the worker was down.
/// </summary>
public static class PlanEmailSchedule
{
    public static readonly TimeOnly SendAfterLocal = new(6, 0);

    public static DateOnly MonthStart(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>Monday of the week containing <paramref name="day"/> (ISO-style week start).</summary>
    public static DateOnly WeekStartMonday(DateOnly day)
    {
        var offset = ((int)day.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return day.AddDays(-offset);
    }

    public static bool IsPastSendTime(DateTimeOffset utcNow)
    {
        var local = BusinessTime.ToLocal(utcNow).DateTime;
        return TimeOnly.FromDateTime(local) >= SendAfterLocal;
    }

    /// <summary>Month (1st) to send a monthly summary for now, or null before 06:00 Accra.</summary>
    public static DateOnly? MonthlyPeriodIfDue(DateTimeOffset utcNow) =>
        IsPastSendTime(utcNow) ? MonthStart(BusinessTime.Today(utcNow)) : null;

    /// <summary>Monday to send a weekly summary for now, or null before 06:00 Accra.</summary>
    public static DateOnly? WeeklyPeriodIfDue(DateTimeOffset utcNow) =>
        IsPastSendTime(utcNow) ? WeekStartMonday(BusinessTime.Today(utcNow)) : null;

    /// <summary>Today's calendar date for due emails, or null before 06:00 Accra.</summary>
    public static DateOnly? DueDateIfDue(DateTimeOffset utcNow) =>
        IsPastSendTime(utcNow) ? BusinessTime.Today(utcNow) : null;
}
