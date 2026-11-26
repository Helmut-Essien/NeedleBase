using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Email;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Identity;
using NeedleBase.Infrastructure.Licensing;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Email;

/// <summary>
/// Paid-plan emails: Owner monthly/weekly summaries and staff assignment/due notices.
/// Gated by <see cref="PlanCatalog"/>; skipped when SMTP is off, the shop is closed, or the licence is read-only.
/// </summary>
public sealed class PlanEmailService : IPlanEmailNotifier
{
    public const int SummaryBatchSize = 40;
    public const int DueBatchSize = 80;

    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _email;
    private readonly IOptionsMonitor<EmailOptions> _emailOptions;
    private readonly IOptionsMonitor<PlatformOptions> _platform;
    private readonly IClock _clock;
    private readonly ILogger<PlanEmailService> _logger;

    public PlanEmailService(
        ApplicationDbContext db,
        IEmailSender email,
        IOptionsMonitor<EmailOptions> emailOptions,
        IOptionsMonitor<PlatformOptions> platform,
        IClock clock,
        ILogger<PlanEmailService> logger)
    {
        _db = db;
        _email = email;
        _emailOptions = emailOptions;
        _platform = platform;
        _clock = clock;
        _logger = logger;
    }

    public async Task NotifyAssignmentAsync(
        string businessId,
        string orderId,
        string displayRef,
        string customerName,
        DateOnly? dueDate,
        string? previousAssigneeUserId,
        string? assigneeUserId,
        CancellationToken cancellationToken = default)
    {
        if (!_email.IsConfigured || string.IsNullOrWhiteSpace(businessId) || string.IsNullOrWhiteSpace(assigneeUserId))
        {
            return;
        }

        assigneeUserId = assigneeUserId.Trim();
        previousAssigneeUserId = string.IsNullOrWhiteSpace(previousAssigneeUserId) ? null : previousAssigneeUserId.Trim();
        if (string.Equals(previousAssigneeUserId, assigneeUserId, StringComparison.Ordinal))
        {
            return;
        }

        var business = await _db.Businesses.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken).ConfigureAwait(false);
        if (business is null || !MaySendPlanEmails(business) ||
            !PlanCatalog.Resolve(business).Entitlements.AssignmentAndDueEmails)
        {
            return;
        }

        var assignee = await (
                from m in _db.StaffMemberships.IgnoreQueryFilters().AsNoTracking()
                where m.BusinessId == businessId && m.UserId == assigneeUserId && m.IsActive
                join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                select new { u.Email, u.FirstName, u.Surname })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (assignee is null || string.IsNullOrWhiteSpace(assignee.Email))
        {
            return;
        }

        var name = StaffNameDto.Compose(assignee.FirstName, assignee.Surname);
        if (!_email.TryQueue(EmailTemplates.OrderAssigned(
                assignee.Email, name, business.Name, displayRef, customerName, dueDate, _emailOptions.CurrentValue.AppBaseUrl)))
        {
            _logger.LogWarning("Assignment email not queued (SMTP queue full) for order {OrderId}", orderId);
        }
    }

    public async Task NotifyBulkAssignmentAsync(
        string businessId,
        string? previousAssigneeUserId,
        string assigneeUserId,
        IReadOnlyList<PlanEmailAssignedOrder> orders,
        CancellationToken cancellationToken = default)
    {
        if (orders is null || orders.Count == 0)
        {
            return;
        }

        if (orders.Count == 1)
        {
            var one = orders[0];
            await NotifyAssignmentAsync(
                businessId, one.OrderId, one.DisplayRef, one.CustomerName, one.DueDate,
                previousAssigneeUserId, assigneeUserId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_email.IsConfigured || string.IsNullOrWhiteSpace(businessId) || string.IsNullOrWhiteSpace(assigneeUserId))
        {
            return;
        }

        assigneeUserId = assigneeUserId.Trim();
        previousAssigneeUserId = string.IsNullOrWhiteSpace(previousAssigneeUserId) ? null : previousAssigneeUserId.Trim();
        if (string.Equals(previousAssigneeUserId, assigneeUserId, StringComparison.Ordinal))
        {
            return;
        }

        var business = await _db.Businesses.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken).ConfigureAwait(false);
        if (business is null || !MaySendPlanEmails(business) ||
            !PlanCatalog.Resolve(business).Entitlements.AssignmentAndDueEmails)
        {
            return;
        }

        var assignee = await (
                from m in _db.StaffMemberships.IgnoreQueryFilters().AsNoTracking()
                where m.BusinessId == businessId && m.UserId == assigneeUserId && m.IsActive
                join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                select new { u.Email, u.FirstName, u.Surname })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (assignee is null || string.IsNullOrWhiteSpace(assignee.Email))
        {
            return;
        }

        const int sampleLimit = 8;
        var samples = orders
            .Take(sampleLimit)
            .Select(o => (o.DisplayRef, o.CustomerName))
            .ToList();
        var name = StaffNameDto.Compose(assignee.FirstName, assignee.Surname);
        if (!_email.TryQueue(EmailTemplates.OrdersAssignedBulk(
                assignee.Email, name, business.Name, orders.Count, samples, sampleLimit,
                _emailOptions.CurrentValue.AppBaseUrl)))
        {
            _logger.LogWarning(
                "Bulk assignment digest not queued (SMTP queue full) for {Count} orders in shop {BusinessId}",
                orders.Count, businessId);
        }
    }

    /// <summary>One worker pass: due emails, then monthly, then weekly. Returns how many messages were queued.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        if (!_email.IsConfigured)
        {
            return 0;
        }

        var now = _clock.UtcNow;
        var queued = 0;
        queued += await SendDueEmailsAsync(now, cancellationToken).ConfigureAwait(false);
        queued += await SendMonthlySummariesAsync(now, cancellationToken).ConfigureAwait(false);
        queued += await SendWeeklySummariesAsync(now, cancellationToken).ConfigureAwait(false);
        return queued;
    }

    private async Task<int> SendDueEmailsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = PlanEmailSchedule.DueDateIfDue(now);
        if (today is null)
        {
            return 0;
        }

        var candidates = await (
                from o in _db.Orders.IgnoreQueryFilters().AsNoTracking()
                where o.DueDate == today
                      && o.DueEmailSentForDate != today
                      && o.AssignedToUserId != null
                      && o.Status != OrderStatus.Collected
                      && o.Status != OrderStatus.Cancelled
                join b in _db.Businesses.AsNoTracking() on o.BusinessId equals b.Id
                where b.ClosedAtUtc == null
                join c in _db.Customers.IgnoreQueryFilters().AsNoTracking() on o.CustomerId equals c.Id
                orderby o.Id
                select new
                {
                    OrderId = o.Id,
                    o.BusinessId,
                    o.DisplayRef,
                    CustomerName = c.Name,
                    o.AssignedToUserId,
                    Business = b
                })
            .Take(DueBatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var queued = 0;
        foreach (var row in candidates)
        {
            if (!MaySendPlanEmails(row.Business) ||
                !PlanCatalog.Resolve(row.Business).Entitlements.AssignmentAndDueEmails)
            {
                // Mark so we don't re-scan forever on Starter shops.
                await MarkDueSentAsync(row.OrderId, today.Value, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var assignee = await (
                    from m in _db.StaffMemberships.IgnoreQueryFilters().AsNoTracking()
                    where m.BusinessId == row.BusinessId && m.UserId == row.AssignedToUserId && m.IsActive
                    join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                    select new { u.Email, u.FirstName, u.Surname })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (assignee is null || string.IsNullOrWhiteSpace(assignee.Email))
            {
                await MarkDueSentAsync(row.OrderId, today.Value, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var name = StaffNameDto.Compose(assignee.FirstName, assignee.Surname);
            // Claim first so a crash after queue cannot double-send; roll back if the channel is full.
            if (!await TryMarkDueSentAsync(row.OrderId, today.Value, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (!_email.TryQueue(EmailTemplates.OrderDue(
                    assignee.Email, name, row.Business.Name, row.DisplayRef, row.CustomerName, today.Value,
                    _emailOptions.CurrentValue.AppBaseUrl)))
            {
                await ClearDueSentAsync(row.OrderId, cancellationToken).ConfigureAwait(false);
                break;
            }

            queued++;
        }

        return queued;
    }

    private async Task MarkDueSentAsync(string orderId, DateOnly day, CancellationToken cancellationToken) =>
        await TryMarkDueSentAsync(orderId, day, cancellationToken).ConfigureAwait(false);

    private async Task<bool> TryMarkDueSentAsync(string orderId, DateOnly day, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || order.DueEmailSentForDate == day)
        {
            return false;
        }

        order.MarkDueEmailSent(day);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task ClearDueSentAsync(string orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return;
        }

        order.ClearDueEmailSent();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> SendMonthlySummariesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var period = PlanEmailSchedule.MonthlyPeriodIfDue(now);
        if (period is null)
        {
            return 0;
        }

        var shops = await _db.Businesses.AsNoTracking()
            .Where(b => b.ClosedAtUtc == null &&
                        (b.MonthlySummarySentFor == null || b.MonthlySummarySentFor < period))
            .OrderBy(b => b.Id)
            .Take(SummaryBatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var queued = 0;
        foreach (var snapshot in shops)
        {
            if (!MaySendPlanEmails(snapshot) || !PlanCatalog.Resolve(snapshot).Entitlements.MonthlySummaryEmail)
            {
                await MarkMonthlyAsync(snapshot.Id, period.Value, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var owner = await LoadOwnerAsync(snapshot.Id, cancellationToken).ConfigureAwait(false);
            if (owner is null)
            {
                await MarkMonthlyAsync(snapshot.Id, period.Value, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var stats = await LoadSummaryStatsAsync(snapshot.Id, now, period.Value, forWeek: false, cancellationToken)
                .ConfigureAwait(false);
            var previous = snapshot.MonthlySummarySentFor;
            if (!await TryMarkMonthlyAsync(snapshot.Id, period.Value, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (!_email.TryQueue(EmailTemplates.MonthlySummary(
                    owner.Value.Email, owner.Value.Name, snapshot.Name, period.Value,
                    stats.Active, stats.Overdue, stats.DueSoon, stats.Fittings,
                    stats.Outstanding, stats.WithBalance, stats.ReceivedPeriod,
                    _emailOptions.CurrentValue.AppBaseUrl)))
            {
                await RestoreMonthlyAsync(snapshot.Id, previous, cancellationToken).ConfigureAwait(false);
                break;
            }

            queued++;
        }

        return queued;
    }

    private async Task<int> SendWeeklySummariesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var period = PlanEmailSchedule.WeeklyPeriodIfDue(now);
        if (period is null)
        {
            return 0;
        }

        var shops = await _db.Businesses.AsNoTracking()
            .Where(b => b.ClosedAtUtc == null &&
                        (b.WeeklySummarySentFor == null || b.WeeklySummarySentFor < period))
            .OrderBy(b => b.Id)
            .Take(SummaryBatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var queued = 0;
        foreach (var snapshot in shops)
        {
            if (!MaySendPlanEmails(snapshot) || !PlanCatalog.Resolve(snapshot).Entitlements.WeeklySummaryEmail)
            {
                await MarkWeeklyAsync(snapshot.Id, period.Value, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var owner = await LoadOwnerAsync(snapshot.Id, cancellationToken).ConfigureAwait(false);
            if (owner is null)
            {
                await MarkWeeklyAsync(snapshot.Id, period.Value, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var stats = await LoadSummaryStatsAsync(snapshot.Id, now, period.Value, forWeek: true, cancellationToken)
                .ConfigureAwait(false);
            var previous = snapshot.WeeklySummarySentFor;
            if (!await TryMarkWeeklyAsync(snapshot.Id, period.Value, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (!_email.TryQueue(EmailTemplates.WeeklySummary(
                    owner.Value.Email, owner.Value.Name, snapshot.Name, period.Value,
                    stats.Active, stats.Overdue, stats.DueSoon,
                    stats.Outstanding, stats.WithBalance, stats.ReceivedPeriod,
                    _emailOptions.CurrentValue.AppBaseUrl)))
            {
                await RestoreWeeklyAsync(snapshot.Id, previous, cancellationToken).ConfigureAwait(false);
                break;
            }

            queued++;
        }

        return queued;
    }

    private async Task MarkMonthlyAsync(string businessId, DateOnly period, CancellationToken cancellationToken) =>
        await TryMarkMonthlyAsync(businessId, period, cancellationToken).ConfigureAwait(false);

    private async Task<bool> TryMarkMonthlyAsync(string businessId, DateOnly period, CancellationToken cancellationToken)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken)
            .ConfigureAwait(false);
        if (business is null || business.MonthlySummarySentFor >= period)
        {
            return false;
        }

        business.MarkMonthlySummarySent(period);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task RestoreMonthlyAsync(string businessId, DateOnly? previous, CancellationToken cancellationToken)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken)
            .ConfigureAwait(false);
        if (business is null)
        {
            return;
        }

        business.RestoreMonthlySummarySent(previous);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkWeeklyAsync(string businessId, DateOnly period, CancellationToken cancellationToken) =>
        await TryMarkWeeklyAsync(businessId, period, cancellationToken).ConfigureAwait(false);

    private async Task<bool> TryMarkWeeklyAsync(string businessId, DateOnly period, CancellationToken cancellationToken)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken)
            .ConfigureAwait(false);
        if (business is null || business.WeeklySummarySentFor >= period)
        {
            return false;
        }

        business.MarkWeeklySummarySent(period);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task RestoreWeeklyAsync(string businessId, DateOnly? previous, CancellationToken cancellationToken)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken)
            .ConfigureAwait(false);
        if (business is null)
        {
            return;
        }

        business.RestoreWeeklySummarySent(previous);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string Email, string? Name)?> LoadOwnerAsync(string businessId, CancellationToken cancellationToken)
    {
        var row = await (
                from m in _db.StaffMemberships.IgnoreQueryFilters().AsNoTracking()
                where m.BusinessId == businessId && m.IsActive && m.Role == BusinessRole.Owner
                join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                select new { u.Email, u.FirstName, u.Surname })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null || string.IsNullOrWhiteSpace(row.Email))
        {
            return null;
        }

        return (row.Email, StaffNameDto.Compose(row.FirstName, row.Surname));
    }

    private async Task<SummaryStats> LoadSummaryStatsAsync(
        string businessId,
        DateTimeOffset now,
        DateOnly periodStart,
        bool forWeek,
        CancellationToken cancellationToken)
    {
        var today = BusinessTime.Today(now);
        var weekEnd = today.AddDays(7);
        var orders = _db.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.BusinessId == businessId);
        var open = orders.Where(o => o.Status != OrderStatus.Collected && o.Status != OrderStatus.Cancelled);

        var active = await open.CountAsync(cancellationToken).ConfigureAwait(false);
        var overdue = await open.CountAsync(o => o.DueDate != null && o.DueDate < today, cancellationToken)
            .ConfigureAwait(false);
        var dueSoon = await open.CountAsync(o => o.DueDate != null && o.DueDate >= today && o.DueDate <= weekEnd, cancellationToken)
            .ConfigureAwait(false);
        var fittings = forWeek
            ? 0
            : await open.CountAsync(o => o.FittingDate != null && o.FittingDate >= today && o.FittingDate <= weekEnd, cancellationToken)
                .ConfigureAwait(false);

        var balances = orders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .Select(o => o.TotalAmount -
                         (_db.Payments.IgnoreQueryFilters()
                             .Where(p => p.OrderId == o.Id && !p.IsVoided).Sum(p => (decimal?)p.Amount) ?? 0m))
            .Where(left => left > 0);
        var agg = await balances.GroupBy(_ => 1)
            .Select(g => new { Sum = g.Sum(), Count = g.Count() })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset receivedFrom;
        DateTimeOffset receivedTo;
        if (forWeek)
        {
            receivedFrom = BusinessTime.StartOfDayUtc(today.AddDays(-6));
            receivedTo = BusinessTime.StartOfDayUtc(today.AddDays(1));
        }
        else
        {
            receivedFrom = BusinessTime.StartOfDayUtc(periodStart);
            var nextMonth = periodStart.AddMonths(1);
            receivedTo = now < BusinessTime.StartOfDayUtc(nextMonth)
                ? now.AddSeconds(1)
                : BusinessTime.StartOfDayUtc(nextMonth);
        }

        var received = await _db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.BusinessId == businessId && !p.IsVoided &&
                        p.RecordedAtUtc >= receivedFrom && p.RecordedAtUtc < receivedTo)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;

        return new SummaryStats(active, overdue, dueSoon, fittings, agg?.Sum ?? 0m, agg?.Count ?? 0, received);
    }

    private bool MaySendPlanEmails(Business business)
    {
        if (business.IsClosed)
        {
            return false;
        }

        var status = SubscriptionRules.Compute(business, _clock.UtcNow, _platform.CurrentValue.LockGraceDays);
        return !status.IsReadOnly;
    }

    private sealed record SummaryStats(
        int Active,
        int Overdue,
        int DueSoon,
        int Fittings,
        decimal Outstanding,
        int WithBalance,
        decimal ReceivedPeriod);
}
