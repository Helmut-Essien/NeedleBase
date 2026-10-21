using NeedleBase.Domain.Common;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Application.Customers;

public sealed class CustomerService : ICustomerService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public CustomerService(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<PaginatedResponse<CustomerListItemDto>> ListAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        (page, pageSize) = PaginatedResponse<CustomerListItemDto>.Normalize(page, pageSize);

        var query = _db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = CustomerSearchFilter.Apply(query, CustomerSearchKeys.Parse(search));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerListItemDto(c.Id, c.Name, c.Phone, c.DateModifiedUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PaginatedResponse<CustomerListItemDto>.Create(items, total, page, pageSize);
    }

    public async Task<CustomerDetailDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureBusiness();

        var customer = await _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id,
                c.BusinessId,
                c.Name,
                c.Phone,
                c.Notes,
                c.PhotoRef,
                c.CreatedAtUtc,
                c.DateModifiedUtc,
                MeasurementCount = _db.MeasurementProfiles.Count(m => m.CustomerId == c.Id)
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            return null;
        }

        return new CustomerDetailDto(
            customer.Id,
            customer.BusinessId,
            customer.Name,
            customer.Phone,
            customer.Notes,
            customer.PhotoRef,
            customer.CreatedAtUtc,
            customer.DateModifiedUtc,
            customer.MeasurementCount);
    }

    /// <summary>Customers whose number is the same as <paramref name="phone"/> (any formatting), for a duplicate warning.</summary>
    public async Task<IReadOnlyList<CustomerListItemDto>> FindByPhoneAsync(
        string? phone,
        string? excludeId,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var digits = CustomerSearchKeys.PhoneDigits(PhoneNumbers.Normalize(phone));
        if (digits.Length < PhoneNumbers.MinDigits - 1)
        {
            return [];
        }

        var normalized = PhoneNumbers.Normalize(phone);
        var rows = await _db.Customers.AsNoTracking()
            .Where(c => EF.Property<string>(c, CustomerSearchKeys.PhoneDigitsColumn) == digits || c.Phone == normalized)
            .Where(c => excludeId == null || c.Id != excludeId)
            .OrderBy(c => c.Name)
            .Take(5)
            .Select(c => new CustomerListItemDto(c.Id, c.Name, c.Phone, c.DateModifiedUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Where(c => PhoneNumbers.Same(c.Phone, normalized)).ToList();
    }

    public async Task<RecordResult<CustomerDetailDto>> CreateAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var businessId = EnsureBusiness();
        var now = _clock.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? IdGenerator.NewId() : request.Id.Trim();

        var existing = await _db.Customers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.BusinessId, businessId, StringComparison.Ordinal))
            {
                return RecordResult.Fail(RecordError.CustomerConflict);
            }

            return await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        }

        if (!request.Lenient && PhoneNumbers.Check(request.Phone) is { } phoneError)
        {
            return RecordResult.Fail(RecordError.PhoneInvalid, phoneError);
        }

        try
        {
            var entity = Customer.Create(
                id,
                businessId,
                request.Name,
                request.Phone,
                request.Notes,
                request.PhotoRef,
                now);
            _db.Customers.Add(entity);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var detail = await GetByIdAsync(entity.Id, cancellationToken).ConfigureAwait(false);
            return detail;
        }
        catch (DbUpdateException)
        {
            var raced = await _db.Customers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                .ConfigureAwait(false);
            if (raced is not null && string.Equals(raced.BusinessId, businessId, StringComparison.Ordinal))
            {
                return await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            }

            return RecordResult.Fail(RecordError.CustomerConflict);
        }
        catch (ArgumentException ex)
        {
            return RecordResult.Fail(RecordError.Validation, ex.Message);
        }
    }

    public async Task<RecordResult<CustomerDetailDto>> UpdateAsync(
        string id,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var entity = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return RecordResult.Fail(RecordError.CustomerNotFound);
        }

        if (StaleUpdate.IsStale(request.BaseModifiedUtc, entity.DateModifiedUtc))
        {
            return RecordResult.Fail(RecordError.Stale);
        }

        // Only a changed number must pass the format rule, so an old customer's notes stay editable.
        if (!request.Lenient && !PhoneNumbers.Same(request.Phone, entity.Phone) &&
            PhoneNumbers.Check(request.Phone) is { } phoneError)
        {
            return RecordResult.Fail(RecordError.PhoneInvalid, phoneError);
        }

        try
        {
            entity.Update(request.Name, request.Phone, request.Notes, request.PhotoRef, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var detail = await GetByIdAsync(entity.Id, cancellationToken).ConfigureAwait(false);
            return detail;
        }
        catch (ArgumentException ex)
        {
            return RecordResult.Fail(RecordError.Validation, ex.Message);
        }
    }

    public async Task<CustomerDeleteResult> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var entity = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return CustomerDeleteResult.NotFound;
        }

        // Orders reference customers with FK Restrict: never orphan or cascade-delete orders.
        if (await _db.Orders.AnyAsync(o => o.CustomerId == id, cancellationToken).ConfigureAwait(false))
        {
            return CustomerDeleteResult.HasOrders;
        }

        // MeasurementProfiles cascade via FK OnDelete(Cascade).
        _db.Customers.Remove(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Defensive: an order was added between the check and the delete (FK violation).
            if (await _db.Orders.AnyAsync(o => o.CustomerId == id, cancellationToken).ConfigureAwait(false))
            {
                return CustomerDeleteResult.HasOrders;
            }

            throw;
        }

        return CustomerDeleteResult.Deleted;
    }

    private string EnsureBusiness()
    {
        if (string.IsNullOrEmpty(_currentUser.BusinessId))
        {
            throw new InvalidOperationException("Business context is required.");
        }

        return _currentUser.BusinessId;
    }
}
