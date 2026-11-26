using NeedleBase.Application.Contracts.Dashboard;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Contracts.Reports;

/// <summary>Money recorded with one payment method in the month (non-voided payments only).</summary>
public sealed record MethodTotalDto(string Method, decimal Amount, int Count)
{
    public string MethodDisplay => PaymentMethod.DisplayName(Method);
    public string AmountDisplay => Money.Format(Amount);
}

/// <summary>One month in the comparison (oldest first).</summary>
public sealed record MonthTrendDto(string Month, string Label, decimal MoneyReceived, int OrdersTaken, decimal OrdersTakenValue)
{
    public string MoneyReceivedDisplay => Money.Format(MoneyReceived);
    public string OrdersTakenValueDisplay => Money.Format(OrdersTakenValue);
}

/// <summary>
/// Monthly report (Workshop / Production / unlimited; Owner and Manager). Months are Accra calendar months.
/// Money received = non-voided payments by their recorded date. Orders taken = orders created in the month that
/// aren't cancelled; their value is the order totals. Still owed = what's left to pay on those orders today
/// (never negative per order). NeedleBase doesn't record the shop's costs, so this is money in, not profit after costs.
/// </summary>
public sealed record MonthlyReportDto(
    string Month,
    string MonthLabel,
    bool IsCurrentMonth,
    string? PreviousMonth,
    string? NextMonth,
    decimal MoneyReceived,
    int PaymentCount,
    decimal PreviousMoneyReceived,
    IReadOnlyList<MethodTotalDto> ByMethod,
    int OrdersTaken,
    decimal OrdersTakenValue,
    decimal StillOwed,
    int OrdersStillOwing,
    int OrdersCancelled,
    IReadOnlyList<StatusCountDto> OrdersByStatus,
    IReadOnlyList<MonthTrendDto> Trend)
{
    public string MoneyReceivedDisplay => Money.Format(MoneyReceived);
    public string OrdersTakenValueDisplay => Money.Format(OrdersTakenValue);
    public string StillOwedDisplay => Money.Format(StillOwed);

    /// <summary>"GH₵ 120.00 more than October 2026", "… less than …", or "Same as …".</summary>
    public string ChangeDisplay
    {
        get
        {
            var previous = Trend.Count >= 2 ? Trend[^2].Label : "last month";
            var diff = MoneyReceived - PreviousMoneyReceived;
            return diff switch
            {
                > 0 => $"{Money.Format(diff)} more than {previous}",
                < 0 => $"{Money.Format(-diff)} less than {previous}",
                _ => $"Same as {previous}"
            };
        }
    }

    /// <summary>True when nothing happened in the month (no payments, no orders created).</summary>
    public bool IsEmpty => PaymentCount == 0 && OrdersTaken == 0 && OrdersCancelled == 0;
}
