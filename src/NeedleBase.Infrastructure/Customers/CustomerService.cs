using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Domain.Entities;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Customers;

public sealed class CustomerService : ICustomerService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public CustomerService(ApplicationDbContext db, ICurrentUser currentUser, IClock clock)
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
            var term = search.Trim();
            query = query.Where(c => c.Name.Contains(term) || c.Phone.Contains(term));
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

    public async Task<(CustomerDetailDto? Customer, string? Error)> CreateAsync(
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
                return (null, "Customer id conflict.");
            }

            return (await GetByIdAsync(id, cancellationToken).ConfigureAwait(false), null);
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
            return (detail, null);
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
                return (await GetByIdAsync(id, cancellationToken).ConfigureAwait(false), null);
            }

            return (null, "Customer id conflict.");
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(CustomerDetailDto? Customer, string? Error)> UpdateAsync(
        string id,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var entity = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return (null, "Customer not found.");
        }

        try
        {
            entity.Update(request.Name, request.Phone, request.Notes, request.PhotoRef, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var detail = await GetByIdAsync(entity.Id, cancellationToken).ConfigureAwait(false);
            return (detail, null);
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
        var entity = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return (false, "Customer not found.");
        }

        // MeasurementProfiles cascade via FK OnDelete(Cascade).
        _db.Customers.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (true, null);
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
