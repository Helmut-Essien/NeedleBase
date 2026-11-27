using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Email;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Tests;

public class PlanEmailScheduleTests
{
    [Fact]
    public void WeekStartMonday_rolls_back_to_monday()
    {
        Assert.Equal(new DateOnly(2026, 11, 23), PlanEmailSchedule.WeekStartMonday(new DateOnly(2026, 11, 26))); // Thursday
        Assert.Equal(new DateOnly(2026, 11, 23), PlanEmailSchedule.WeekStartMonday(new DateOnly(2026, 11, 23))); // Monday
        Assert.Equal(new DateOnly(2026, 11, 23), PlanEmailSchedule.WeekStartMonday(new DateOnly(2026, 11, 29))); // Sunday
    }

    [Fact]
    public void MonthStart_is_first_of_month()
    {
        Assert.Equal(new DateOnly(2026, 11, 1), PlanEmailSchedule.MonthStart(new DateOnly(2026, 11, 26)));
    }

    [Theory]
    [InlineData(5, 59, false)]
    [InlineData(6, 0, true)]
    [InlineData(12, 0, true)]
    public void IsPastSendTime_uses_accra_0600(int hour, int minute, bool expected)
    {
        var utc = new DateTimeOffset(2026, 11, 26, hour, minute, 0, TimeSpan.Zero);
        Assert.Equal(expected, PlanEmailSchedule.IsPastSendTime(utc));
        Assert.Equal(expected ? new DateOnly(2026, 11, 26) : null, PlanEmailSchedule.DueDateIfDue(utc));
        Assert.Equal(expected ? new DateOnly(2026, 11, 1) : null, PlanEmailSchedule.MonthlyPeriodIfDue(utc));
        Assert.Equal(expected ? new DateOnly(2026, 11, 23) : null, PlanEmailSchedule.WeeklyPeriodIfDue(utc));
    }
}

public class PlanEmailTemplateTests
{
    [Fact]
    public void Monthly_weekly_assignment_and_due_templates_are_plain_and_escaped()
    {
        var monthly = EmailTemplates.MonthlySummary(
            "ama@shop.gh", "Ama <b>", "Ama & Sons", new DateOnly(2026, 11, 1),
            12, 2, 5, 1, 1250m, 4, 3400m, "https://app.needlebase.test/");
        Assert.Equal("monthly_summary", monthly.Kind);
        Assert.Contains("November 2026", monthly.TextBody);
        Assert.Contains("GH₵ 1,250.00", monthly.TextBody);
        Assert.Contains("Ama &amp; Sons", monthly.HtmlBody);
        Assert.DoesNotContain("<b>", monthly.HtmlBody);

        Assert.DoesNotContain("Profit", monthly.TextBody);
        Assert.DoesNotContain("Spent", monthly.TextBody);

        var withProfit = EmailTemplates.MonthlySummary(
            "ama@shop.gh", "Ama", "Ama Stitches", new DateOnly(2026, 11, 1),
            12, 2, 5, 1, 0m, 0, 0m, "https://app.needlebase.test/",
            new MonthProfitSummary(new DateOnly(2026, 10, 1), 5000m, 3200.5m));
        Assert.Contains("Last month (October 2026)", withProfit.TextBody);
        Assert.Contains("Money in: GH₵ 5,000.00\nSpent: GH₵ 3,200.50\nProfit: GH₵ 1,799.50", withProfit.TextBody);
        Assert.Contains("https://app.needlebase.test/Reports?month=2026-10", withProfit.TextBody);
        Assert.Contains("See the monthly report", withProfit.HtmlBody);

        var loss = EmailTemplates.MonthlySummary(
            "ama@shop.gh", null, "Ama Stitches", new DateOnly(2026, 11, 1),
            0, 0, 0, 0, 0m, 0, 0m, null,
            new MonthProfitSummary(new DateOnly(2026, 10, 1), 100m, 250m));
        Assert.Contains("Loss: GH₵ 150.00", loss.TextBody);
        Assert.DoesNotContain("-GH₵", loss.TextBody);
        Assert.DoesNotContain("Monthly report:", loss.TextBody); // no app link configured

        var noCosts = EmailTemplates.MonthlySummary(
            "ama@shop.gh", null, "Ama Stitches", new DateOnly(2026, 11, 1),
            0, 0, 0, 0, 0m, 0, 0m, null,
            new MonthProfitSummary(new DateOnly(2026, 10, 1), 100m, 0m));
        Assert.Contains("No expenses were recorded for October 2026, so profit equals money in.", noCosts.TextBody);

        var weekly = EmailTemplates.WeeklySummary(
            "ama@shop.gh", "Ama", "Ama Stitches", new DateOnly(2026, 11, 23),
            10, 1, 3, 500m, 2, 200m, null);
        Assert.Equal("weekly_summary", weekly.Kind);
        Assert.Contains("Balances owed: GH₵ 500.00 across 2 orders", weekly.TextBody);
        Assert.Contains("23 Nov – 29 Nov 2026", weekly.TextBody);

        var assigned = EmailTemplates.OrderAssigned(
            "esi@shop.gh", "Esi", "Ama Stitches", "ABCD1234", "Kofi <script>", new DateOnly(2026, 11, 26),
            "https://app.needlebase.test");
        Assert.Equal("order_assigned", assigned.Kind);
        Assert.Contains("#ABCD1234", assigned.TextBody);
        Assert.Contains("Kofi &lt;script&gt;", assigned.HtmlBody);
        Assert.Contains("https://app.needlebase.test/Orders", assigned.TextBody);

        var due = EmailTemplates.OrderDue(
            "esi@shop.gh", null, "Ama Stitches", "ABCD1234", "Kofi", new DateOnly(2026, 11, 26), null);
        Assert.Equal("order_due", due.Kind);
        Assert.Contains("due today", due.TextBody);
        Assert.StartsWith("Hello,", due.TextBody);

        var bulk = EmailTemplates.OrdersAssignedBulk(
            "esi@shop.gh", "Esi", "Ama & Sons", 12,
            [("A1", "Kofi <b>"), ("A2", "Yaw"), ("A3", "Ama")], 2, "https://app.needlebase.test");
        Assert.Equal("order_assigned_bulk", bulk.Kind);
        Assert.Contains("12 orders", bulk.TextBody);
        Assert.Contains("…and 10 more", bulk.TextBody);
        Assert.Contains("Kofi &lt;b&gt;", bulk.HtmlBody);
        Assert.DoesNotContain("#A3", bulk.TextBody); // sample limit 2
    }
}

public class PlanEmailDomainWatermarkTests
{
    [Fact]
    public void Business_and_order_watermarks_validate_and_clear()
    {
        var business = Business.Create("01ARZ3NDEKTSV4RRFFQ69G5FBZ", "Ama", null, DateTimeOffset.UtcNow);
        business.MarkMonthlySummarySent(new DateOnly(2026, 11, 1));
        business.MarkWeeklySummarySent(new DateOnly(2026, 11, 23));
        Assert.Equal(new DateOnly(2026, 11, 1), business.MonthlySummarySentFor);
        Assert.Equal(new DateOnly(2026, 11, 23), business.WeeklySummarySentFor);
        Assert.Throws<ArgumentException>(() => business.MarkMonthlySummarySent(new DateOnly(2026, 11, 2)));
        Assert.Throws<ArgumentException>(() => business.MarkWeeklySummarySent(new DateOnly(2026, 11, 24)));

        var orderId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
        var order = Order.Create(
            orderId, business.Id, "01ARZ3NDEKTSV4RRFFQ69G5FC1",
            new DateOnly(2026, 11, 26), null, "user1", null,
            [OrderLine.Create("01ARZ3NDEKTSV4RRFFQ69G5FA1", business.Id, orderId, GarmentTemplate.Dress, 1, 100m, null, MeasurementUnit.Centimetres, "{}", null)],
            DateTimeOffset.UtcNow);
        order.MarkDueEmailSent(new DateOnly(2026, 11, 26));
        Assert.Equal(new DateOnly(2026, 11, 26), order.DueEmailSentForDate);
        order.UpdateHeader(new DateOnly(2026, 11, 27), null, "user1", null, DateTimeOffset.UtcNow);
        Assert.Null(order.DueEmailSentForDate);

        order.MarkDueEmailSent(new DateOnly(2026, 11, 27));
        order.Reassign("user2", DateTimeOffset.UtcNow); // same due day, new assignee
        Assert.Null(order.DueEmailSentForDate);
        Assert.Equal("user2", order.AssignedToUserId);

        order.MarkDueEmailSent(new DateOnly(2026, 11, 27));
        order.UpdateHeader(new DateOnly(2026, 11, 27), null, "user2", "note", DateTimeOffset.UtcNow); // same assignee
        Assert.Equal(new DateOnly(2026, 11, 27), order.DueEmailSentForDate);
    }
}

public class PlanEmailAssignmentBufferTests
{
    [Fact]
    public async Task Clear_drops_pending_without_notify_Flush_sends_once()
    {
        var buffer = new PlanEmailAssignmentBuffer();
        var notifier = new RecordingNotifier();
        buffer.Enqueue("biz", "ord1", "ABCD", "Kofi", null, null, "user1");
        buffer.Clear();
        await buffer.FlushAsync(notifier);
        Assert.Empty(notifier.Calls);

        buffer.Enqueue("biz", "ord1", "ABCD", "Kofi", null, null, "user1");
        buffer.Enqueue("biz", "ord2", "EFGH", "Yaw", null, "user1", "user2");
        await buffer.FlushAsync(notifier);
        Assert.Equal(2, notifier.Calls.Count);
        await buffer.FlushAsync(notifier); // already drained
        Assert.Equal(2, notifier.Calls.Count);
    }

    private sealed class RecordingNotifier : IPlanEmailNotifier
    {
        public List<string> Calls { get; } = [];

        public Task NotifyAssignmentAsync(
            string businessId, string orderId, string displayRef, string customerName, DateOnly? dueDate,
            string? previousAssigneeUserId, string? assigneeUserId, CancellationToken cancellationToken = default)
        {
            Calls.Add(orderId);
            return Task.CompletedTask;
        }

        public Task NotifyBulkAssignmentAsync(
            string businessId, string? previousAssigneeUserId, string assigneeUserId,
            IReadOnlyList<PlanEmailAssignedOrder> orders, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
