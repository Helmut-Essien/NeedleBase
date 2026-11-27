using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Services;

public sealed class FreeUpgradeLocalService : IFreeUpgradeLocalService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public FreeUpgradeLocalService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<bool> HasShopRowsAsync(string businessId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return await db.Customers.AsNoTracking().AnyAsync(c => c.BusinessId == businessId, cancellationToken).ConfigureAwait(false)
            || await db.Orders.AsNoTracking().AnyAsync(o => o.BusinessId == businessId, cancellationToken).ConfigureAwait(false)
            || await db.MeasurementProfiles.AsNoTracking().AnyAsync(m => m.BusinessId == businessId, cancellationToken).ConfigureAwait(false)
            || await db.Payments.AsNoTracking().AnyAsync(p => p.BusinessId == businessId, cancellationToken).ConfigureAwait(false)
            || await db.Expenses.AsNoTracking().AnyAsync(e => e.BusinessId == businessId, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemapBusinessIdAsync(
        string fromBusinessId, string toBusinessId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromBusinessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toBusinessId);
        if (string.Equals(fromBusinessId, toBusinessId, StringComparison.Ordinal))
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        db.SuppressSyncQueue = true;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Parameterised ids only — table/column names are fixed in code.
                string[] tables =
                [
                    "Customers",
                    "MeasurementProfiles",
                    "Orders",
                    "OrderLines",
                    "Payments",
                    "Expenses",
                    "SyncQueue",
                    "SyncDeferred",
                    "WhatsAppConsents",
                    "DeviceInfo"
                ];

                foreach (var table in tables)
                {
                    var sql = $"UPDATE \"{table}\" SET \"BusinessId\" = {{0}} WHERE \"BusinessId\" = {{1}}";
                    await db.Database.ExecuteSqlRawAsync(sql, [toBusinessId, fromBusinessId], cancellationToken)
                        .ConfigureAwait(false);
                }

                await db.Database.ExecuteSqlRawAsync(
                        "DELETE FROM \"FreeModeStates\" WHERE \"BusinessId\" = {0}",
                        [fromBusinessId],
                        cancellationToken)
                    .ConfigureAwait(false);

                // Drop any FreeModeStates row that might already use the online id (should not exist).
                await db.Database.ExecuteSqlRawAsync(
                        "DELETE FROM \"FreeModeStates\" WHERE \"BusinessId\" = {0}",
                        [toBusinessId],
                        cancellationToken)
                    .ConfigureAwait(false);

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            db.SuppressSyncQueue = false;
        }

        LocalDataVersion.Bump();
    }
}
