using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Services;

public sealed class LocalDatabaseService : ILocalDatabaseService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LocalDbOptions _options;

    public LocalDatabaseService(IServiceScopeFactory scopeFactory, LocalDbOptions options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        await LocalSchemaMigrator.MigrateAsync(db, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountUnsyncedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            var queued = await db.SyncQueue.CountAsync(cancellationToken).ConfigureAwait(false);
            // Rows created before sync existed (never queued, never seen by the server) are unsynced too.
            var queuedIds = db.SyncQueue.Select(q => q.EntityId);
            var unadopted =
                await db.Customers.CountAsync(c => !EF.Property<bool>(c, LocalDbContext.ServerKnownColumn) && !queuedIds.Contains(c.Id), cancellationToken).ConfigureAwait(false) +
                await db.MeasurementProfiles.CountAsync(m => !EF.Property<bool>(m, LocalDbContext.ServerKnownColumn) && !queuedIds.Contains(m.Id), cancellationToken).ConfigureAwait(false) +
                await db.Orders.CountAsync(o => !EF.Property<bool>(o, LocalDbContext.ServerKnownColumn) && !queuedIds.Contains(o.Id), cancellationToken).ConfigureAwait(false);
            return queued + unadopted;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return 0; // no readable store: nothing to protect
        }
    }

    public async Task<bool> WipeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
            await LocalSchemaMigrator.MigrateAsync(db, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            // Last resort: delete the file if EF cannot open a corrupted store.
            try
            {
                if (File.Exists(_options.DbPath))
                {
                    File.Delete(_options.DbPath);
                }

                await MigrateAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
