using System.Globalization;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Dashboard;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Reports;
using NeedleBase.Application.Payments;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Reports;

/// <summary>Who may see monthly reports, which months, and the stable error codes (server, web and phone).</summary>
public static class ReportRules
{
    /// <summary>Months in the comparison, ending with the chosen month.</summary>
    public const int TrendMonths = 6;

    public const string NotAllowedCode = "REPORTS_NOT_ALLOWED";
    public const string ForbiddenCode = "REPORTS_FORBIDDEN";
    public const string MonthInvalidCode = "REPORT_MONTH_INVALID";

    public const string NotAllowedMessage = "Monthly reports come with the Workshop and Production plans.";
    public const string ForbiddenMessage = "Only the owner or a manager can see monthly reports.";
    public const string MonthInvalidMessage = "Pick a month from January 2020 up to this month.";

    /// <summary>
    /// Old note from before expenses (step 7b). Still shown by the phone and web report screens until they show
    /// Spent and Profit (expenses plan chunks 7 and 8), then removed.
    /// </summary>
    public const string CostsNote =
        "NeedleBase doesn't record your costs (fabric, wages, rent) yet, so this shows money in, not profit after costs.";

    /// <summary>Shown with the profit figure when the month has expenses.</summary>
    public const string ProfitNote =
        "Profit is money in minus the expenses you recorded for the month. Money still owed and costs you haven't recorded aren't counted.";

    /// <summary>Shown with the profit figure when the month has no expenses.</summary>
    public const string NoExpensesNote = "No expenses recorded this month, so profit equals money in.";

    public static string ProfitNoteFor(bool hasExpenses) => hasExpenses ? ProfitNote : NoExpensesNote;

    public static readonly DateOnly EarliestMonth = new(2020, 1, 1);

    /// <summary>Same people who see money elsewhere: Owner and Manager.</summary>
    public static bool CanView(string? role) => PaymentRules.CanManage(role);

    public static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>The Accra calendar month an instant falls in.</summary>
    public static DateOnly MonthOf(DateTimeOffset utc) => FirstOfMonth(BusinessTime.Today(utc));

    public static string Key(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static string Label(DateOnly month) => month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>yyyy-MM</c> (blank = this month). False when malformed, before <see cref="EarliestMonth"/> or after this month.
    /// </summary>
    public static bool TryResolveMonth(string? value, DateOnly today, out DateOnly month)
    {
        var current = FirstOfMonth(today);
        month = current;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var v = value.Trim();
        if (v.Length != 7 || v[4] != '-' ||
            !int.TryParse(v.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(v.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) ||
            year < 1 || m is < 1 or > 12)
        {
            return false;
        }

        var candidate = new DateOnly(year, m, 1);
        if (candidate < EarliestMonth || candidate > current)
        {
            return false;
        }

        month = candidate;
        return true;
    }

    /// <summary>UTC range [from, to) covering the comparison months that end with <paramref name="month"/>.</summary>
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) Window(DateOnly month)
    {
        month = FirstOfMonth(month);
        return (BusinessTime.StartOfDayUtc(month.AddMonths(-(TrendMonths - 1))), BusinessTime.StartOfDayUtc(month.AddMonths(1)));
    }
}

/// <summary>An order created in the report window. <see cref="PaidToDate"/> = all its non-voided payments, any date.</summary>
public sealed record ReportOrderRow(string Status, decimal TotalAmount, DateTimeOffset CreatedAtUtc, decimal PaidToDate);

/// <summary>A non-voided payment recorded in the report window.</summary>
public sealed record ReportPaymentRow(decimal Amount, string Method, DateTimeOffset RecordedAtUtc);

/// <summary>An expense dated in the report window (Accra date paid). Voided rows are ignored by the calculator.</summary>
public sealed record ReportExpenseRow(decimal Amount, string Category, DateOnly SpentOn, bool IsVoided);

/// <summary>
/// Pure month maths shared by the Api (Postgres rows) and the phone (SQLite rows), so both show the same numbers.
/// Rows outside the window are ignored; callers pass non-voided payments only.
/// </summary>
public static class MonthlyReportCalculator
{
    public static MonthlyReportDto Build(
        DateOnly month,
        DateOnly today,
        IEnumerable<ReportOrderRow> orders,
        IEnumerable<ReportPaymentRow> payments,
        IEnumerable<ReportExpenseRow>? expenses = null)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(payments);
        month = ReportRules.FirstOfMonth(month);
        var current = ReportRules.FirstOfMonth(today);
        var months = Enumerable.Range(0, ReportRules.TrendMonths)
            .Select(i => month.AddMonths(i - (ReportRules.TrendMonths - 1)))
            .ToList();

        var orderList = orders.Select(o => (Month: ReportRules.MonthOf(o.CreatedAtUtc), Row: o)).ToList();
        var paymentList = payments.Select(p => (Month: ReportRules.MonthOf(p.RecordedAtUtc), Row: p)).ToList();
        var expenseList = (expenses ?? [])
            .Where(e => !e.IsVoided)
            .Select(e => (Month: ReportRules.FirstOfMonth(e.SpentOn), Row: e))
            .ToList();

        var trend = months.Select(m =>
        {
            var takenInMonth = orderList.Where(o => o.Month == m && o.Row.Status != OrderStatus.Cancelled).Select(o => o.Row).ToList();
            return new MonthTrendDto(
                ReportRules.Key(m),
                ReportRules.Label(m),
                paymentList.Where(p => p.Month == m).Sum(p => p.Row.Amount),
                takenInMonth.Count,
                takenInMonth.Sum(o => o.TotalAmount),
                expenseList.Where(e => e.Month == m).Sum(e => e.Row.Amount));
        }).ToList();

        var monthExpenses = expenseList.Where(e => e.Month == month).Select(e => e.Row).ToList();
        var byCategory = monthExpenses
            .GroupBy(e => ExpenseCategory.IsValid(e.Category) ? e.Category : ExpenseCategory.Other)
            .Select(g => new CategoryTotalDto(g.Key, g.Sum(e => e.Amount), g.Count()))
            .OrderByDescending(t => t.Total)
            .ThenBy(t => CategoryOrder(t.Category))
            .ToList();

        var monthPayments = paymentList.Where(p => p.Month == month).Select(p => p.Row).ToList();
        var monthOrders = orderList.Where(o => o.Month == month).Select(o => o.Row).ToList();
        var taken = monthOrders.Where(o => o.Status != OrderStatus.Cancelled).ToList();
        var owing = taken.Select(o => o.TotalAmount - o.PaidToDate).Where(left => left > 0).ToList();

        var byMethod = monthPayments
            .GroupBy(p => PaymentMethod.IsValid(p.Method) ? p.Method : PaymentMethod.Other)
            .Select(g => new MethodTotalDto(g.Key, g.Sum(p => p.Amount), g.Count()))
            .OrderBy(t => MethodOrder(t.Method))
            .ToList();

        var byStatus = OrderStatus.All
            .Select(s => new StatusCountDto(s, monthOrders.Count(o => o.Status == s)))
            .ToList();

        return new MonthlyReportDto(
            Month: ReportRules.Key(month),
            MonthLabel: ReportRules.Label(month),
            IsCurrentMonth: month == current,
            PreviousMonth: month > ReportRules.EarliestMonth ? ReportRules.Key(month.AddMonths(-1)) : null,
            NextMonth: month < current ? ReportRules.Key(month.AddMonths(1)) : null,
            MoneyReceived: trend[^1].MoneyReceived,
            PaymentCount: monthPayments.Count,
            PreviousMoneyReceived: trend.Count >= 2 ? trend[^2].MoneyReceived : 0m,
            ByMethod: byMethod,
            OrdersTaken: taken.Count,
            OrdersTakenValue: taken.Sum(o => o.TotalAmount),
            StillOwed: owing.Sum(),
            OrdersStillOwing: owing.Count,
            OrdersCancelled: monthOrders.Count(o => o.Status == OrderStatus.Cancelled),
            OrdersByStatus: byStatus,
            Trend: trend,
            Spent: trend[^1].Spent,
            ExpenseCount: monthExpenses.Count,
            PreviousSpent: trend.Count >= 2 ? trend[^2].Spent : 0m,
            SpentByCategory: byCategory);
    }

    private static int CategoryOrder(string category)
    {
        var i = ExpenseCategory.All.ToList().IndexOf(category);
        return i < 0 ? ExpenseCategory.All.Count : i;
    }

    private static int MethodOrder(string method)
    {
        for (var i = 0; i < PaymentMethod.All.Count; i++)
        {
            if (PaymentMethod.All[i] == method)
            {
                return i;
            }
        }

        return PaymentMethod.All.Count;
    }
}
