using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Application.Customers;

public sealed class MeasurementService : IMeasurementService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public MeasurementService(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<(PaginatedResponse<MeasurementListItemDto>? Page, string? Error)> ListForCustomerAsync(
        string customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        (page, pageSize) = PaginatedResponse<MeasurementListItemDto>.Normalize(page, pageSize);

        var customerExists = await _db.Customers.AsNoTracking()
            .AnyAsync(c => c.Id == customerId, cancellationToken)
            .ConfigureAwait(false);
        if (!customerExists)
        {
            return (null, "Customer not found.");
        }

        var query = _db.MeasurementProfiles.AsNoTracking().Where(m => m.CustomerId == customerId);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(m => m.MeasuredAtUtc)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MeasurementListItemDto(
                m.Id,
                m.CustomerId,
                m.GarmentTemplate,
                m.Unit,
                m.MeasuredAtUtc,
                m.DateModifiedUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (PaginatedResponse<MeasurementListItemDto>.Create(items, total, page, pageSize), null);
    }

    public async Task<MeasurementDetailDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var entity = await _db.MeasurementProfiles.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : MapDetail(entity);
    }

    public async Task<(MeasurementDetailDto? Measurement, string? Error)> CreateAsync(
        string customerId,
        CreateMeasurementRequest request,
        CancellationToken cancellationToken = default)
    {
        var businessId = EnsureBusiness();
        var customerExists = await _db.Customers.AsNoTracking()
            .AnyAsync(c => c.Id == customerId, cancellationToken)
            .ConfigureAwait(false);
        if (!customerExists)
        {
            return (null, "Customer not found.");
        }

        var id = string.IsNullOrWhiteSpace(request.Id) ? IdGenerator.NewId() : request.Id.Trim();
        var existing = await _db.MeasurementProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.BusinessId, businessId, StringComparison.Ordinal))
            {
                return (null, "Measurement id conflict.");
            }

            return (await GetByIdAsync(id, cancellationToken).ConfigureAwait(false), null);
        }

        var now = _clock.UtcNow;
        var measuredAt = request.MeasuredAtUtc ?? now;
        try
        {
            var fieldsJson = MeasurementFieldsSerializer.Serialize(request.Fields);
            var entity = MeasurementProfile.Create(
                id,
                businessId,
                customerId,
                request.GarmentTemplate,
                request.Unit,
                fieldsJson,
                request.Notes,
                measuredAt,
                now);
            _db.MeasurementProfiles.Add(entity);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (MapDetail(entity), null);
        }
        catch (DbUpdateException)
        {
            var raced = await _db.MeasurementProfiles
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
                .ConfigureAwait(false);
            if (raced is not null && string.Equals(raced.BusinessId, businessId, StringComparison.Ordinal))
            {
                return (await GetByIdAsync(id, cancellationToken).ConfigureAwait(false), null);
            }

            return (null, "Measurement id conflict.");
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(MeasurementDetailDto? Measurement, string? Error)> UpdateAsync(
        string id,
        UpdateMeasurementRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var entity = await _db.MeasurementProfiles.FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return (null, "Measurement not found.");
        }

        var now = _clock.UtcNow;
        var measuredAt = request.MeasuredAtUtc ?? entity.MeasuredAtUtc;
        try
        {
            var fieldsJson = MeasurementFieldsSerializer.Serialize(request.Fields);
            entity.Update(
                request.GarmentTemplate,
                request.Unit,
                fieldsJson,
                request.Notes,
                measuredAt,
                now);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (MapDetail(entity), null);
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var entity = await _db.MeasurementProfiles.FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return (false, "Measurement not found.");
        }

        _db.MeasurementProfiles.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (true, null);
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

    private string EnsureBusiness()
    {
        if (string.IsNullOrEmpty(_currentUser.BusinessId))
        {
            throw new InvalidOperationException("Business context is required.");
        }

        return _currentUser.BusinessId;
    }
}
