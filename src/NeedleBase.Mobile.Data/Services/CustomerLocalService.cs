using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Domain.Entities;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Services;

public sealed class CustomerLocalService : ICustomerLocalService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CustomerLocalService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<IReadOnlyList<CustomerListItemDto>> SearchAsync(
        string businessId,
        string? search,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        take = Math.Clamp(take, 1, 100);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var parsed = CustomerSearchKeys.Parse(search);
        var query = CustomerSearchFilter.Apply(
            db.Customers.AsNoTracking().Where(c => c.BusinessId == businessId),
            parsed);

        // SQLite's ADO provider is synchronous under async; keep the query off the UI thread.
        return await Task.Run(
            () => CustomerSearchFilter.Rank(query, parsed)
            .Take(take)
            .Select(c => new CustomerListItemDto(c.Id, c.Name, c.Phone, c.DateModifiedUtc))
            .ToListAsync(cancellationToken),
            cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CustomerListItemDto>> RecentAsync(
        string businessId,
        int take = 8,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        take = Math.Clamp(take, 1, 30);

        return await Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            // Customers with the newest orders first (order Ids are time-sortable ULIDs)...
            var recentIds = await db.Orders.AsNoTracking()
                .Where(o => o.BusinessId == businessId)
                .GroupBy(o => o.CustomerId)
                .Select(g => new { CustomerId = g.Key, Last = g.Max(o => o.Id) })
                .OrderByDescending(x => x.Last)
                .Take(take)
                .Select(x => x.CustomerId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var byOrder = await db.Customers.AsNoTracking()
                .Where(c => c.BusinessId == businessId && recentIds.Contains(c.Id))
                .Select(c => new CustomerListItemDto(c.Id, c.Name, c.Phone, c.DateModifiedUtc))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var result = byOrder.OrderBy(c => recentIds.IndexOf(c.Id)).ToList();

            // ...then the newest customers to fill the list.
            if (result.Count < take)
            {
                var fill = await db.Customers.AsNoTracking()
                    .Where(c => c.BusinessId == businessId && !recentIds.Contains(c.Id))
                    .OrderByDescending(c => c.Id) // ULID = newest added (SQLite can't ORDER BY DateTimeOffset)
                    .Take(take - result.Count)
                    .Select(c => new CustomerListItemDto(c.Id, c.Name, c.Phone, c.DateModifiedUtc))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                result.AddRange(fill);
            }

            return (IReadOnlyList<CustomerListItemDto>)result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CustomerDetailDto?> GetByIdAsync(
        string businessId,
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (customer is null)
        {
            return null;
        }

        var count = await db.MeasurementProfiles.AsNoTracking()
            .CountAsync(m => m.BusinessId == businessId && m.CustomerId == id, cancellationToken)
            .ConfigureAwait(false);

        return MapDetail(customer, count);
    }

    public async Task<(CustomerDetailDto? Customer, string? Error)> SaveAsync(
        string businessId,
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        var now = DateTimeOffset.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? IdGenerator.NewId() : request.Id.Trim();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var existing = await db.Customers
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            try
            {
                existing.Update(request.Name, request.Phone, request.Notes, request.PhotoRef, now);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return (await GetByIdAsync(businessId, id, cancellationToken).ConfigureAwait(false), null);
            }
            catch (ArgumentException ex)
            {
                return (null, ex.Message);
            }
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
            db.Customers.Add(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (MapDetail(entity, 0), null);
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(CustomerDetailDto? Customer, string? Error)> UpdateAsync(
        string businessId,
        string id,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var entity = await db.Customers
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return (null, "Customer not found.");
        }

        try
        {
            entity.Update(request.Name, request.Phone, request.Notes, request.PhotoRef, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (await GetByIdAsync(businessId, id, cancellationToken).ConfigureAwait(false), null);
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
    }

    private static CustomerDetailDto MapDetail(Customer c, int measurementCount) =>
        new(
            c.Id,
            c.BusinessId,
            c.Name,
            c.Phone,
            c.Notes,
            c.PhotoRef,
            c.CreatedAtUtc,
            c.DateModifiedUtc,
            measurementCount);

    public async Task<CustomerDeleteResult> DeleteAsync(
        string businessId,
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var entity = await db.Customers
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return CustomerDeleteResult.NotFound;
        }

        if (await db.Orders.AnyAsync(o => o.BusinessId == businessId && o.CustomerId == id, cancellationToken)
                .ConfigureAwait(false))
        {
            return CustomerDeleteResult.HasOrders;
        }

        db.Customers.Remove(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CustomerDeleteResult.Deleted;
    }
}
