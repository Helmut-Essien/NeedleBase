using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Licensing;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Sync;

namespace NeedleBase.Mobile.Data.Services;

public sealed class FreeModeLocalService : IFreeModeLocalService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public FreeModeLocalService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<bool> ExistsAsync(string businessId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return await db.FreeModeStates.AsNoTracking()
            .AnyAsync(s => s.BusinessId == businessId, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task CreateAsync(string businessId, string shopName, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        EnsureCoreAsync(businessId, shopName, now, seedFromExistingRows: false, cancellationToken);

    public Task EnsureAsync(string businessId, string shopName, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        EnsureCoreAsync(businessId, shopName, now, seedFromExistingRows: true, cancellationToken);

    private async Task EnsureCoreAsync(
        string businessId,
        string shopName,
        DateTimeOffset now,
        bool seedFromExistingRows,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(shopName);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        if (await db.FreeModeStates.AnyAsync(s => s.BusinessId == businessId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var customersEver = 0;
        var ordersEver = 0;
        if (seedFromExistingRows)
        {
            // Lifetime ≥ rows still on the device. Deleted rows are unknowable; this never undercuts
            // the remaining budget below what is still present (fail closed for a wiped FreeModeStates).
            customersEver = await db.Customers.AsNoTracking()
                .CountAsync(c => c.BusinessId == businessId, cancellationToken)
                .ConfigureAwait(false);
            ordersEver = await db.Orders.AsNoTracking()
                .CountAsync(o => o.BusinessId == businessId, cancellationToken)
                .ConfigureAwait(false);
            customersEver = Math.Min(customersEver, FreePlanLimits.MaxCustomersEver);
            ordersEver = Math.Min(ordersEver, FreePlanLimits.MaxOrdersEver);
        }

        db.SuppressSyncQueue = true;
        try
        {
            db.FreeModeStates.Add(new FreeModeState
            {
                BusinessId = businessId,
                ShopName = shopName.Trim(),
                CustomersEverCreated = customersEver,
                OrdersEverCreated = ordersEver,
                CreatedAtUtc = now
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            db.SuppressSyncQueue = false;
        }
    }

    public async Task<(int CustomersUsed, int OrdersUsed)> GetUsageAsync(
        string businessId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var row = await db.FreeModeStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BusinessId == businessId, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? (0, 0) : (row.CustomersEverCreated, row.OrdersEverCreated);
    }

    public Task<RecordFailure?> TryReserveCustomerSlotAsync(
        string businessId, CancellationToken cancellationToken = default) =>
        TryReserveAsync(businessId, customers: true, cancellationToken);

    public Task ReleaseCustomerSlotAsync(string businessId, CancellationToken cancellationToken = default) =>
        ReleaseAsync(businessId, customers: true, cancellationToken);

    public Task<RecordFailure?> TryReserveOrderSlotAsync(
        string businessId, CancellationToken cancellationToken = default) =>
        TryReserveAsync(businessId, customers: false, cancellationToken);

    public Task ReleaseOrderSlotAsync(string businessId, CancellationToken cancellationToken = default) =>
        ReleaseAsync(businessId, customers: false, cancellationToken);

    private async Task<RecordFailure?> TryReserveAsync(
        string businessId, bool customers, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        if (!LocalFreeModeGate.IsFreeBusiness(businessId))
        {
            return null;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        if (!await db.FreeModeStates.AsNoTracking()
                .AnyAsync(s => s.BusinessId == businessId, cancellationToken)
                .ConfigureAwait(false))
        {
            // Free session without counters: fail closed (never unlimited).
            return customers
                ? RecordResult.Fail(RecordError.FreeCustomerLimit, FreePlanLimits.CustomerLimitReachedMessage)
                : RecordResult.Fail(RecordError.FreeOrderLimit, FreePlanLimits.OrderLimitReachedMessage);
        }

        var max = customers ? FreePlanLimits.MaxCustomersEver : FreePlanLimits.MaxOrdersEver;
        // Parameterised: BusinessId + max only. Column choice is fixed in code, not user input.
        var sql = customers
            ? "UPDATE \"FreeModeStates\" SET \"CustomersEverCreated\" = \"CustomersEverCreated\" + 1 WHERE \"BusinessId\" = {0} AND \"CustomersEverCreated\" < {1}"
            : "UPDATE \"FreeModeStates\" SET \"OrdersEverCreated\" = \"OrdersEverCreated\" + 1 WHERE \"BusinessId\" = {0} AND \"OrdersEverCreated\" < {1}";
        var affected = await db.Database.ExecuteSqlRawAsync(sql, [businessId, max], cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return customers
                ? RecordResult.Fail(RecordError.FreeCustomerLimit, FreePlanLimits.CustomerLimitReachedMessage)
                : RecordResult.Fail(RecordError.FreeOrderLimit, FreePlanLimits.OrderLimitReachedMessage);
        }

        return null;
    }

    private async Task ReleaseAsync(string businessId, bool customers, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        if (!LocalFreeModeGate.IsFreeBusiness(businessId))
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var sql = customers
            ? "UPDATE \"FreeModeStates\" SET \"CustomersEverCreated\" = CASE WHEN \"CustomersEverCreated\" > 0 THEN \"CustomersEverCreated\" - 1 ELSE 0 END WHERE \"BusinessId\" = {0}"
            : "UPDATE \"FreeModeStates\" SET \"OrdersEverCreated\" = CASE WHEN \"OrdersEverCreated\" > 0 THEN \"OrdersEverCreated\" - 1 ELSE 0 END WHERE \"BusinessId\" = {0}";
        await db.Database.ExecuteSqlRawAsync(sql, [businessId], cancellationToken).ConfigureAwait(false);
    }
}
