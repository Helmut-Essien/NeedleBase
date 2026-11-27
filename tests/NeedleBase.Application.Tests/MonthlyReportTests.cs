using NeedleBase.Application.Common;
using NeedleBase.Application.Licensing;
using NeedleBase.Application.Reports;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Tests;

/// <summary>Plan step 7b: month maths shared by the Api and the phone, month parsing and plan entitlement.</summary>
public class MonthlyReportTests
{
    private static readonly DateOnly Nov = new(2026, 11, 1);
    private static readonly DateOnly Today = new(2026, 11, 26);

    private static DateTimeOffset At(int year, int month, int day, int hour = 10) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Empty_month_is_all_zero_with_six_months_and_every_status()
    {
        var r = MonthlyReportCalculator.Build(Nov, Today, [], []);
        Assert.True(r.IsEmpty);
        Assert.Equal(0m, r.MoneyReceived);
        Assert.Equal(0, r.PaymentCount);
        Assert.Equal(0m, r.StillOwed);
        Assert.Empty(r.ByMethod);
        Assert.Equal(OrderStatus.All, r.OrdersByStatus.Select(s => s.Status));
        Assert.All(r.OrdersByStatus, s => Assert.Equal(0, s.Count));
        Assert.Equal(["2026-06", "2026-07", "2026-08", "2026-09", "2026-10", "2026-11"], r.Trend.Select(t => t.Month));
        Assert.Equal("Same as October 2026", r.ChangeDisplay);
        Assert.Equal(("2026-11", "November 2026", true), (r.Month, r.MonthLabel, r.IsCurrentMonth));
    }

    [Fact]
    public void Expenses_give_spent_profit_and_category_breakdown_biggest_first()
    {
        ReportPaymentRow[] payments =
        [
            new(1000m, PaymentMethod.Cash, At(2026, 11, 5)),
            new(40m, PaymentMethod.Cash, At(2026, 10, 5))
        ];
        ReportExpenseRow[] expenses =
        [
            new(300m, ExpenseCategory.Materials, new DateOnly(2026, 11, 2), false),
            new(500m, ExpenseCategory.StaffPay, new DateOnly(2026, 11, 20), false),
            new(100m, ExpenseCategory.Materials, new DateOnly(2026, 11, 21), false),
            new(999m, ExpenseCategory.RentAndBills, new DateOnly(2026, 11, 3), true),  // voided: ignored
            new(20m, "Snacks", new DateOnly(2026, 11, 4), false),                      // unknown code → Other
            new(50m, ExpenseCategory.Transport, new DateOnly(2026, 10, 9), false)      // October
        ];

        var r = MonthlyReportCalculator.Build(Nov, Today, [], payments, expenses);

        Assert.Equal(920m, r.Spent);
        Assert.Equal(4, r.ExpenseCount);
        Assert.True(r.HasExpenses);
        Assert.Equal(80m, r.Profit);
        Assert.False(r.IsLoss);
        Assert.Equal(("Profit", "GH₵ 80.00", "GH₵ 920.00"), (r.ProfitLabel, r.ProfitDisplay, r.SpentDisplay));
        Assert.Equal(50m, r.PreviousSpent);
        Assert.Equal(-10m, r.PreviousProfit);
        Assert.Equal("GH₵ 90.00 better than October 2026", r.ProfitChangeDisplay);
        Assert.Equal(ReportRules.ProfitNote, r.ProfitNote);
        Assert.False(r.IsEmpty);

        Assert.Equal(
            [(ExpenseCategory.StaffPay, 500m, 1), (ExpenseCategory.Materials, 400m, 2), (ExpenseCategory.Other, 20m, 1)],
            r.SpentByCategory.Select(c => (c.Category, c.Total, c.Count)));
        Assert.Equal("Fabric and materials", r.SpentByCategory[1].Label);

        var oct = r.Trend[^2];
        Assert.Equal((50m, -10m, true, "Loss", "GH₵ 10.00"), (oct.Spent, oct.Profit, oct.IsLoss, oct.ProfitLabel, oct.ProfitDisplay));
        Assert.Equal((920m, 80m), (r.Trend[^1].Spent, r.Trend[^1].Profit));
    }

    [Fact]
    public void Spending_more_than_money_in_is_a_loss_shown_without_minus()
    {
        var r = MonthlyReportCalculator.Build(Nov, Today, [], [new(100m, PaymentMethod.MoMo, At(2026, 11, 3))],
            [new(250m, ExpenseCategory.RentAndBills, new DateOnly(2026, 11, 1), false)]);
        Assert.Equal(-150m, r.Profit);
        Assert.True(r.IsLoss);
        Assert.Equal(("Loss", "GH₵ 150.00"), (r.ProfitLabel, r.ProfitDisplay));
        Assert.Equal("GH₵ 150.00 worse than October 2026", r.ProfitChangeDisplay);
    }

    [Fact]
    public void Voided_only_expenses_count_as_none_and_month_stays_empty()
    {
        var r = MonthlyReportCalculator.Build(Nov, Today, [], [],
            [new(80m, ExpenseCategory.Transport, new DateOnly(2026, 11, 10), true)]);
        Assert.Equal((0m, 0, false), (r.Spent, r.ExpenseCount, r.HasExpenses));
        Assert.Empty(r.SpentByCategory);
        Assert.Equal(ReportRules.NoExpensesNote, r.ProfitNote);
        Assert.True(r.IsEmpty);
    }

    [Fact]
    public void Expense_only_month_is_not_empty()
    {
        var r = MonthlyReportCalculator.Build(Nov, Today, [], [],
            [new(80m, ExpenseCategory.Transport, new DateOnly(2026, 11, 10), false)]);
        Assert.False(r.IsEmpty);
        Assert.Equal(-80m, r.Profit);
    }

    [Fact]
    public void Expenses_group_by_accra_date_with_month_edges_and_window()
    {
        ReportExpenseRow[] expenses =
        [
            new(10m, ExpenseCategory.Other, new DateOnly(2026, 10, 31), false),  // last day of October
            new(20m, ExpenseCategory.Other, new DateOnly(2026, 11, 1), false),   // first day of November
            new(30m, ExpenseCategory.Other, new DateOnly(2026, 6, 1), false),    // first trend month
            new(99m, ExpenseCategory.Other, new DateOnly(2026, 5, 31), false)    // before the window: ignored
        ];

        var r = MonthlyReportCalculator.Build(Nov, Today, [], [], expenses);
        Assert.Equal(20m, r.Spent);
        Assert.Equal(10m, r.PreviousSpent);
        Assert.Equal([30m, 0m, 0m, 0m, 10m, 20m], r.Trend.Select(t => t.Spent));
    }

    [Fact]
    public void No_expenses_means_profit_equals_money_in()
    {
        var r = MonthlyReportCalculator.Build(Nov, Today, [], [new(70m, PaymentMethod.Cash, At(2026, 11, 3))]);
        Assert.Equal((0m, 70m, false), (r.Spent, r.Profit, r.IsLoss));
        Assert.Equal(ReportRules.NoExpensesNote, r.ProfitNote);
        Assert.All(r.Trend, t => Assert.Equal(t.MoneyReceived, t.Profit));
    }

    [Fact]
    public void Money_orders_and_still_owed()
    {
        ReportOrderRow[] orders =
        [
            new(OrderStatus.New, 300m, At(2026, 11, 3), 100m),         // owes 200
            new(OrderStatus.Ready, 200m, At(2026, 11, 10), 200m),      // paid in full
            new(OrderStatus.Collected, 100m, At(2026, 11, 11), 150m),  // overpaid: adds 0, never -50
            new(OrderStatus.Cancelled, 999m, At(2026, 11, 12), 0m),    // not taken
            new(OrderStatus.Sewing, 80m, At(2026, 10, 30), 0m)         // October
        ];
        ReportPaymentRow[] payments =
        [
            new(100m, PaymentMethod.MoMo, At(2026, 11, 3)),
            new(200m, PaymentMethod.Cash, At(2026, 11, 10)),
            new(150m, PaymentMethod.Cash, At(2026, 11, 11)),
            new(40m, PaymentMethod.Bank, At(2026, 10, 2))
        ];

        var r = MonthlyReportCalculator.Build(Nov, Today, orders, payments);
        Assert.Equal(450m, r.MoneyReceived);
        Assert.Equal(3, r.PaymentCount);
        Assert.Equal(40m, r.PreviousMoneyReceived);
        Assert.Equal("GH₵ 410.00 more than October 2026", r.ChangeDisplay);
        Assert.Equal(3, r.OrdersTaken);
        Assert.Equal(600m, r.OrdersTakenValue);
        Assert.Equal(200m, r.StillOwed);
        Assert.Equal(1, r.OrdersStillOwing);
        Assert.Equal(1, r.OrdersCancelled);
        Assert.Equal(1, r.OrdersByStatus.Single(s => s.Status == OrderStatus.Cancelled).Count);
        Assert.Equal([(PaymentMethod.Cash, 350m, 2), (PaymentMethod.MoMo, 100m, 1)], r.ByMethod.Select(m => (m.Method, m.Amount, m.Count)));
        var oct = r.Trend.Single(t => t.Month == "2026-10");
        Assert.Equal((40m, 1, 80m), (oct.MoneyReceived, oct.OrdersTaken, oct.OrdersTakenValue));
        Assert.False(r.IsEmpty);
    }

    [Fact]
    public void Less_than_last_month_wording()
    {
        var r = MonthlyReportCalculator.Build(Nov, Today, [], [new(50m, PaymentMethod.Cash, At(2026, 10, 15))]);
        Assert.Equal("GH₵ 50.00 less than October 2026", r.ChangeDisplay);
    }

    [Fact]
    public void Month_boundaries_use_accra_calendar_days()
    {
        var novStart = BusinessTime.StartOfDayUtc(Nov);
        var decStart = BusinessTime.StartOfDayUtc(Nov.AddMonths(1));
        ReportPaymentRow[] payments =
        [
            new(1m, PaymentMethod.Cash, novStart.AddTicks(-1)), // October, last instant
            new(2m, PaymentMethod.Cash, novStart),              // November, first instant
            new(4m, PaymentMethod.Cash, decStart.AddTicks(-1)), // November, last instant
            new(8m, PaymentMethod.Cash, decStart)               // December: outside the window
        ];
        var r = MonthlyReportCalculator.Build(Nov, Today, [], payments);
        Assert.Equal(6m, r.MoneyReceived);
        Assert.Equal(1m, r.PreviousMoneyReceived);
        Assert.Equal(7m, r.Trend.Sum(t => t.MoneyReceived));
    }

    [Fact]
    public void Rows_before_the_window_are_ignored_and_years_cross()
    {
        var feb = new DateOnly(2026, 2, 1);
        var r = MonthlyReportCalculator.Build(feb, Today, [new(OrderStatus.New, 10m, At(2025, 8, 31), 0m)], [new(5m, PaymentMethod.Cash, At(2025, 8, 31))]);
        Assert.Equal(["2025-09", "2025-10", "2025-11", "2025-12", "2026-01", "2026-02"], r.Trend.Select(t => t.Month));
        Assert.Equal(0m, r.Trend.Sum(t => t.MoneyReceived));
        Assert.Equal(0, r.Trend.Sum(t => t.OrdersTaken));
        Assert.Equal(("2026-01", "2026-03", false), (r.PreviousMonth, r.NextMonth, r.IsCurrentMonth));
    }

    [Fact]
    public void Unknown_method_counts_as_other_after_the_known_ones()
    {
        var r = MonthlyReportCalculator.Build(Nov, Today, [],
        [
            new(5m, "Barter", At(2026, 11, 2)),
            new(7m, PaymentMethod.Other, At(2026, 11, 2)),
            new(3m, PaymentMethod.Bank, At(2026, 11, 2))
        ]);
        Assert.Equal([(PaymentMethod.Bank, 3m, 1), (PaymentMethod.Other, 12m, 2)], r.ByMethod.Select(m => (m.Method, m.Amount, m.Count)));
        Assert.Equal("Bank transfer", r.ByMethod[0].MethodDisplay);
    }

    [Fact]
    public void First_and_current_month_have_no_previous_or_next()
    {
        var first = MonthlyReportCalculator.Build(ReportRules.EarliestMonth, Today, [], []);
        Assert.Null(first.PreviousMonth);
        Assert.Equal("2020-02", first.NextMonth);
        var current = MonthlyReportCalculator.Build(Today, Today, [], []); // any day in the month works
        Assert.Equal("2026-11", current.Month);
        Assert.Null(current.NextMonth);
    }

    [Theory]
    [InlineData(null, "2026-11")]
    [InlineData("", "2026-11")]
    [InlineData("  ", "2026-11")]
    [InlineData("2026-11", "2026-11")]
    [InlineData(" 2026-10 ", "2026-10")]
    [InlineData("2020-01", "2020-01")]
    public void Month_parsing_accepts(string? value, string expected)
    {
        Assert.True(ReportRules.TryResolveMonth(value, Today, out var month));
        Assert.Equal(expected, ReportRules.Key(month));
    }

    [Theory]
    [InlineData("2026-12")]   // future
    [InlineData("2027-01")]
    [InlineData("2019-12")]   // before the earliest month
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("2026-1")]
    [InlineData("2026/11")]
    [InlineData("+202-11")]
    [InlineData("26-11-01")]
    [InlineData("abcd-ef")]
    [InlineData("0000-01")]
    [InlineData("२०२६-११")] // non-ASCII digits
    public void Month_parsing_rejects(string value)
    {
        Assert.False(ReportRules.TryResolveMonth(value, Today, out _));
    }

    [Theory]
    [InlineData(BusinessRole.Owner, true)]
    [InlineData(BusinessRole.Manager, true)]
    [InlineData(BusinessRole.Tailor, false)]
    [InlineData(null, false)]
    [InlineData("owner", false)]
    public void Only_owner_and_manager_view(string? role, bool expected) => Assert.Equal(expected, ReportRules.CanView(role));

    [Theory]
    [InlineData(null, "workshop", false, false, true)]       // keyless → unlimited
    [InlineData("key", "workshop", false, false, true)]
    [InlineData("key", " PRODUCTION ", false, false, true)]
    [InlineData("key", "starter", false, true, true)]        // complimentary → unlimited
    [InlineData("key", "starter", false, false, false)]
    [InlineData("key", "workshop", true, false, false)]      // trial → Starter
    [InlineData("key", null, false, false, false)]           // blank → Starter (fail closed)
    [InlineData("key", "pro", false, false, false)]          // unknown → Starter
    public void Reports_entitlement_by_plan(string? key, string? plan, bool trial, bool complimentary, bool expected) =>
        Assert.Equal(expected, PlanCatalog.Resolve(key, plan, trial, complimentary).Entitlements.ProfitAndMonthlyReports);

    [Fact]
    public void Window_covers_six_accra_months()
    {
        var (from, to) = ReportRules.Window(Today);
        Assert.Equal(BusinessTime.StartOfDayUtc(new DateOnly(2026, 6, 1)), from);
        Assert.Equal(BusinessTime.StartOfDayUtc(new DateOnly(2026, 12, 1)), to);
    }
}
