using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Licensing;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Licensing;

/// <summary>
/// Builds a JSON export of a shop's Business Data for paid / keyless / complimentary shops
/// (including while read-only). Free phone-only shops use the mobile "Save a copy" path instead.
/// </summary>
public sealed class BusinessExportService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ApplicationDbContext _db;

    public BusinessExportService(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// Null when the shop is missing or export is not allowed for its plan.
    /// </summary>
    public async Task<(string? Json, string? FileStem, string? ErrorCode)> BuildAsync(
        string businessId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);

        var business = await _db.Businesses.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken)
            .ConfigureAwait(false);
        if (business is null)
        {
            return (null, null, "BUSINESS_NOT_FOUND");
        }

        var entitlements = PlanCatalog.Resolve(business).Entitlements;
        if (!entitlements.Export)
        {
            return (null, null, "EXPORT_NOT_ALLOWED");
        }

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => c.BusinessId == businessId)
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Phone,
                c.Notes,
                c.CreatedAtUtc,
                c.DateModifiedUtc
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var measurements = await _db.MeasurementProfiles.AsNoTracking()
            .Where(m => m.BusinessId == businessId)
            .OrderBy(m => m.CustomerId)
            .ThenByDescending(m => m.MeasuredAtUtc)
            .Select(m => new
            {
                m.Id,
                m.CustomerId,
                m.GarmentTemplate,
                m.Unit,
                m.FieldsJson,
                m.MeasuredAtUtc,
                m.Notes
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var orders = await _db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.BusinessId == businessId)
            .OrderByDescending(o => o.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var orderRows = orders.Select(o => new
        {
            o.Id,
            o.DisplayRef,
            o.CustomerId,
            o.Status,
            o.DueDate,
            o.FittingDate,
            o.TotalAmount,
            o.Notes,
            o.CreatedAtUtc,
            Lines = o.Lines.Select(l => new
            {
                l.Id,
                l.GarmentTemplate,
                l.Quantity,
                l.UnitPrice,
                l.MeasurementProfileId
            }).ToList()
        }).ToList();

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.BusinessId == businessId)
            .OrderByDescending(p => p.RecordedAtUtc)
            .Select(p => new
            {
                p.Id,
                p.OrderId,
                p.CustomerId,
                p.Amount,
                p.Method,
                p.RecordedAtUtc,
                p.Note,
                p.IsVoided
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var staff = await _db.StaffMemberships.AsNoTracking()
            .Where(m => m.BusinessId == businessId && m.IsActive)
            .Select(m => new { m.UserId, m.Role })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var payload = new
        {
            format = "NeedleBase shop export",
            version = 1,
            exportedAtUtc = DateTimeOffset.UtcNow,
            shopName = business.Name,
            shopPhone = business.Phone,
            businessId = business.Id,
            planCode = entitlements.Code,
            customers,
            measurements,
            orders = orderRows,
            payments,
            staff = staff.Select(s => new { s.UserId, role = s.Role }).ToList()
        };

        var safe = string.Concat(business.Name.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').Take(40));
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "shop";
        }

        var stem = $"needlebase-{safe}-{DateTime.UtcNow:yyyyMMdd}";
        return (JsonSerializer.Serialize(payload, Json), stem, null);
    }
}
