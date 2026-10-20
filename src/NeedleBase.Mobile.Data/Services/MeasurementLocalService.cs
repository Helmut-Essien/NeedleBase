using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Domain.Entities;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Services;

public sealed class MeasurementLocalService : IMeasurementLocalService
{
    private const int DefaultTake = 100;
    private readonly IServiceScopeFactory _scopeFactory;

    public MeasurementLocalService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<IReadOnlyList<MeasurementListItemDto>> ListForCustomerAsync(
        string businessId,
        string customerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        // SQLite cannot ORDER BY DateTimeOffset — materialize all for customer, then
        // sort by MeasuredAtUtc and cap (Take before OrderBy would drop newest rows).
        var rows = await db.MeasurementProfiles.AsNoTracking()
            .Where(m => m.BusinessId == businessId && m.CustomerId == customerId)
            .Select(m => new
            {
                m.Id,
                m.CustomerId,
                m.GarmentTemplate,
                m.Unit,
                m.MeasuredAtUtc,
                m.DateModifiedUtc
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .OrderByDescending(m => m.MeasuredAtUtc)
            .ThenByDescending(m => m.Id)
            .Take(DefaultTake)
            .Select(m => new MeasurementListItemDto(
                m.Id,
                m.CustomerId,
                m.GarmentTemplate,
                m.Unit,
                m.MeasuredAtUtc,
                m.DateModifiedUtc))
            .ToList();
    }

    public async Task<MeasurementDetailDto?> GetByIdAsync(
        string businessId,
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var entity = await db.MeasurementProfiles.AsNoTracking()
            .FirstOrDefaultAsync(m => m.BusinessId == businessId && m.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : MapDetail(entity);
    }

    public async Task<(MeasurementDetailDto? Measurement, string? Error)> SaveAsync(
        string businessId,
        string customerId,
        CreateMeasurementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var customerExists = await db.Customers.AsNoTracking()
            .AnyAsync(c => c.BusinessId == businessId && c.Id == customerId, cancellationToken)
            .ConfigureAwait(false);
        if (!customerExists)
        {
            return (null, "Customer not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? IdGenerator.NewId() : request.Id.Trim();

        try
        {
            AmountRules.EnsureMeasurements(request.Unit, request.Fields);
            var fieldsJson = MeasurementFieldsSerializer.Serialize(request.Fields);
            var existing = await db.MeasurementProfiles
                .FirstOrDefaultAsync(m => m.BusinessId == businessId && m.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                // Preserve historical MeasuredAtUtc unless caller explicitly overrides.
                var measuredAt = request.MeasuredAtUtc ?? existing.MeasuredAtUtc;
                existing.Update(
                    request.GarmentTemplate,
                    request.Unit,
                    fieldsJson,
                    request.Notes,
                    measuredAt,
                    now);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return (MapDetail(existing), null);
            }

            var measuredAtCreate = request.MeasuredAtUtc ?? now;
            var entity = MeasurementProfile.Create(
                id,
                businessId,
                customerId,
                request.GarmentTemplate,
                request.Unit,
                fieldsJson,
                request.Notes,
                measuredAtCreate,
                now);
            db.MeasurementProfiles.Add(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (MapDetail(entity), null);
        }
        catch (Exception ex) when (LocalStorageErrors.IsStorageFull(ex))
        {
            return (null, LocalStorageErrors.StorageFullMessage);
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
    }

    private static MeasurementDetailDto MapDetail(MeasurementProfile entity) =>
        new(
            entity.Id,
            entity.BusinessId,
            entity.CustomerId,
            entity.GarmentTemplate,
            entity.Unit,
            MeasurementFieldsSerializer.Deserialize(entity.FieldsJson),
            entity.Notes,
            entity.MeasuredAtUtc,
            entity.DateModifiedUtc);
}
