using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Dashboard;
using NeedleBase.Application.Payments;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}

/// <summary>Web dashboard numbers (tenant-filtered sets; money only for Owner/Manager).</summary>
public sealed class DashboardService : IDashboardService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public DashboardService(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentUser.BusinessId))
        {
            throw new InvalidOperationException("Business context is required.");
        }

        var today = BusinessTime.Today(_clock.UtcNow);
        var weekEnd = today.AddDays(7);
        var orders = _db.Orders.AsNoTracking();

        var counts = await orders.GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byStatus = OrderStatus.All
            .Select(s => new StatusCountDto(s, counts.Where(c => c.Status == s).Sum(c => c.Count)))
            .ToList();

        var open = orders.Where(o => o.Status != OrderStatus.Collected && o.Status != OrderStatus.Cancelled);
        var overdue = await open.CountAsync(o => o.DueDate != null && o.DueDate < today, cancellationToken).ConfigureAwait(false);
        var dueSoon = await open.CountAsync(o => o.DueDate != null && o.DueDate >= today && o.DueDate <= weekEnd, cancellationToken)
            .ConfigureAwait(false);
        var fittings = await open.CountAsync(o => o.FittingDate != null && o.FittingDate >= today && o.FittingDate <= weekEnd, cancellationToken)
            .ConfigureAwait(false);

        DashboardMoneyDto? money = null;
        if (PaymentRules.CanManage(_currentUser.Role))
        {
            var balances = orders
                .Where(o => o.Status != OrderStatus.Cancelled)
                .Select(o => o.TotalAmount -
                             (_db.Payments.Where(p => p.OrderId == o.Id && !p.IsVoided).Sum(p => (decimal?)p.Amount) ?? 0m))
                .Where(left => left > 0);
            // One aggregate round trip (was two scans of the same correlated query).
            var agg = await balances.GroupBy(_ => 1)
                .Select(g => new { Sum = g.Sum(), Count = g.Count() })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var outstanding = agg?.Sum ?? 0m;
            var withBalance = agg?.Count ?? 0;

            var todayStart = BusinessTime.StartOfDayUtc(today);
            var weekStart = BusinessTime.StartOfDayUtc(today.AddDays(-6));
            var tomorrow = BusinessTime.StartOfDayUtc(today.AddDays(1));
            var received = _db.Payments.AsNoTracking().Where(p => !p.IsVoided && p.RecordedAtUtc < tomorrow);
            var receivedToday = await received.Where(p => p.RecordedAtUtc >= todayStart)
                .SumAsync(p => (decimal?)p.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;
            var receivedWeek = await received.Where(p => p.RecordedAtUtc >= weekStart)
                .SumAsync(p => (decimal?)p.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;
            money = new DashboardMoneyDto(outstanding, withBalance, receivedToday, receivedWeek);
        }

        var active = byStatus.Where(s => !OrderStatus.IsTerminal(s.Status)).Sum(s => s.Count);
        return new DashboardSummaryDto(active, overdue, dueSoon, fittings, byStatus, money, today);
    }
}
