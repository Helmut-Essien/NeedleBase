using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Reports;
using NeedleBase.Application.Reports;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Services;

/// <summary>
/// Profit and monthly reports from this phone's copy of the shop (works offline). Same maths as the server
/// (<see cref="MonthlyReportCalculator"/>); payments still waiting to sync are included.
/// </summary>
public interface IReportLocalService
{
    Task<MonthlyReportDto> GetAsync(string businessId, DateOnly month, DateOnly today, CancellationToken cancellationToken = default);
}

public sealed class ReportLocalService : IReportLocalService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ReportLocalService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<MonthlyReportDto> GetAsync(string businessId, DateOnly month, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        var (from, to) = ReportRules.Window(month);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        // SQLite can't compare DateTimeOffset or sum decimals in SQL: load slim rows, then filter in memory.
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.BusinessId == businessId)
            .Select(o => new { o.Id, o.Status, o.TotalAmount, o.CreatedAtUtc })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.BusinessId == businessId && !p.IsVoided)
            .Select(p => new { p.OrderId, p.Amount, p.Method, p.RecordedAtUtc })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var paidByOrder = payments.GroupBy(p => p.OrderId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount), StringComparer.Ordinal);
        var orderRows = orders
            .Where(o => o.CreatedAtUtc >= from && o.CreatedAtUtc < to)
            .Select(o => new ReportOrderRow(o.Status, o.TotalAmount, o.CreatedAtUtc, paidByOrder.GetValueOrDefault(o.Id)))
            .ToList();
        var paymentRows = payments
            .Where(p => p.RecordedAtUtc >= from && p.RecordedAtUtc < to)
            .Select(p => new ReportPaymentRow(p.Amount, p.Method, p.RecordedAtUtc))
            .ToList();

        return MonthlyReportCalculator.Build(month, today, orderRows, paymentRows);
    }
}
