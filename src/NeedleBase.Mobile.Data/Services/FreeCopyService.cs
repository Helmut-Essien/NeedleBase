using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Services;

public sealed class FreeCopyService : IFreeCopyService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IServiceScopeFactory _scopeFactory;

    public FreeCopyService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<string?> BuildCopyJsonAsync(
        string businessId, string shopName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            var customers = await db.Customers.AsNoTracking()
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

            var measurements = await db.MeasurementProfiles.AsNoTracking()
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

            var orders = await db.Orders.AsNoTracking()
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

            var payments = await db.Payments.AsNoTracking()
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

            var usage = await db.FreeModeStates.AsNoTracking()
                .Where(s => s.BusinessId == businessId)
                .Select(s => new { s.CustomersEverCreated, s.OrdersEverCreated })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var payload = new
            {
                format = "NeedleBase Free copy",
                version = 1,
                exportedAtUtc = DateTimeOffset.UtcNow,
                shopName,
                businessId,
                lifetimeCustomersCreated = usage?.CustomersEverCreated ?? 0,
                lifetimeOrdersCreated = usage?.OrdersEverCreated ?? 0,
                customers,
                measurements,
                orders = orderRows,
                payments
            };

            return JsonSerializer.Serialize(payload, Json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Free copy failed: {ex.Message}");
            return null;
        }
    }
}
