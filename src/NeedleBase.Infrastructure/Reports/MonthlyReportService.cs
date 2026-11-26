using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Reports;
using NeedleBase.Application.Licensing;
using NeedleBase.Application.Reports;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Reports;

/// <summary>
/// Profit and monthly reports (plan step 7b). Owner/Manager on Workshop, Production, complimentary and keyless shops.
/// Two slim, BusinessId-indexed, no-tracking queries for the six-month window; the maths is
/// <see cref="MonthlyReportCalculator"/> (shared with the phone).
/// </summary>
public sealed class MonthlyReportService
{
    public const string BusinessNotFoundCode = "BUSINESS_NOT_FOUND";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public MonthlyReportService(ApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>The report, or a stable error code from <see cref="ReportRules"/> (or <see cref="BusinessNotFoundCode"/>).</summary>
    public async Task<(MonthlyReportDto? Report, string? ErrorCode)> GetAsync(
        string businessId,
        string? role,
        string? month,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        if (!ReportRules.CanView(role))
        {
            return (null, ReportRules.ForbiddenCode);
        }

        var licence = await _db.Businesses.AsNoTracking()
            .Where(b => b.Id == businessId)
            .Select(b => new { b.LicenseKeyProtected, b.LicensePlan, b.LicenseIsTrial, b.LicenseIsComplimentary })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (licence is null)
        {
            return (null, BusinessNotFoundCode);
        }

        var entitlements = PlanCatalog.Resolve(
            licence.LicenseKeyProtected, licence.LicensePlan, licence.LicenseIsTrial, licence.LicenseIsComplimentary).Entitlements;
        if (!entitlements.ProfitAndMonthlyReports)
        {
            return (null, ReportRules.NotAllowedCode);
        }

        var today = BusinessTime.Today(_clock.UtcNow);
        if (!ReportRules.TryResolveMonth(month, today, out var chosen))
        {
            return (null, ReportRules.MonthInvalidCode);
        }

        var (from, to) = ReportRules.Window(chosen);
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.BusinessId == businessId && o.CreatedAtUtc >= from && o.CreatedAtUtc < to)
            .Select(o => new ReportOrderRow(
                o.Status,
                o.TotalAmount,
                o.CreatedAtUtc,
                _db.Payments.Where(p => p.BusinessId == businessId && p.OrderId == o.Id && !p.IsVoided)
                    .Sum(p => (decimal?)p.Amount) ?? 0m))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.BusinessId == businessId && !p.IsVoided && p.RecordedAtUtc >= from && p.RecordedAtUtc < to)
            .Select(p => new ReportPaymentRow(p.Amount, p.Method, p.RecordedAtUtc))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return (MonthlyReportCalculator.Build(chosen, today, orders, payments), null);
    }
}
