using NeedleBase.Application.Contracts.Dashboard;
using NeedleBase.Application.Contracts.Expenses;
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
/// <remarks><see cref="Spent"/> = non-voided expenses dated in the month; <see cref="Profit"/> = money in − spent (may be negative).</remarks>
public sealed record MonthTrendDto(string Month, string Label, decimal MoneyReceived, int OrdersTaken, decimal OrdersTakenValue, decimal Spent)
{
    public decimal Profit => MoneyReceived - Spent;
    public bool IsLoss => Profit < 0;
    public string MoneyReceivedDisplay => Money.Format(MoneyReceived);
    public string OrdersTakenValueDisplay => Money.Format(OrdersTakenValue);
    public string SpentDisplay => Money.Format(Spent);

    /// <summary>"Profit" or "Loss": the amount is shown without a minus sign, the word carries the meaning.</summary>
    public string ProfitLabel => IsLoss ? "Loss" : "Profit";
    public string ProfitDisplay => Money.Format(Math.Abs(Profit));
}

/// <summary>
/// Monthly report (Workshop / Production / unlimited; Owner and Manager). Months are Accra calendar months.
/// Money received = non-voided payments by their recorded date. Orders taken = orders created in the month that
/// aren't cancelled; their value is the order totals. Still owed = what's left to pay on those orders today
/// (never negative per order). Spent = non-voided expenses whose Accra date paid (<c>SpentOn</c>) is in the month.
/// Profit = money received − spent (cash basis; money still owed and unrecorded costs aren't counted).
/// Expense fields were added in step 7b-2 and are additive: older clients ignore them.
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
    IReadOnlyList<MonthTrendDto> Trend,
    decimal Spent,
    int ExpenseCount,
    decimal PreviousSpent,
    IReadOnlyList<CategoryTotalDto> SpentByCategory)
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

    public decimal Profit => MoneyReceived - Spent;
    public decimal PreviousProfit => PreviousMoneyReceived - PreviousSpent;
    public bool IsLoss => Profit < 0;
    public bool HasExpenses => ExpenseCount > 0;
    public string SpentDisplay => Money.Format(Spent);

    /// <summary>"Profit" or "Loss": the amount is shown without a minus sign, the word carries the meaning.</summary>
    public string ProfitLabel => IsLoss ? "Loss" : "Profit";
    public string ProfitDisplay => Money.Format(Math.Abs(Profit));

    /// <summary>"GH₵ 50.00 better than October 2026", "… worse than …", or "Same as …" (profit vs the month before).</summary>
    public string ProfitChangeDisplay
    {
        get
        {
            var previous = Trend.Count >= 2 ? Trend[^2].Label : "last month";
            var diff = Profit - PreviousProfit;
            return diff switch
            {
                > 0 => $"{Money.Format(diff)} better than {previous}",
                < 0 => $"{Money.Format(-diff)} worse than {previous}",
                _ => $"Same as {previous}"
            };
        }
    }

    /// <summary>Plain-language note for the profit figure (depends on whether the month has expenses).</summary>
    public string ProfitNote => NeedleBase.Application.Reports.ReportRules.ProfitNoteFor(HasExpenses);

    /// <summary>True when nothing happened in the month (no payments, no orders created, no expenses).</summary>
    public bool IsEmpty => PaymentCount == 0 && OrdersTaken == 0 && OrdersCancelled == 0 && ExpenseCount == 0;
}
