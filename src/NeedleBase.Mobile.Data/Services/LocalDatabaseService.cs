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
            return await WipeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            NeedleBase.Mobile.Data.Data.LocalDataVersion.Bump(); // cached lists must not show wiped rows
        }
    }

    private async Task<bool> WipeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            var path = FilePath(db);
            await db.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
            // EF deletes only the main file. Close pooled handles, then remove -wal/-shm so no prior tenant's
            // pages survive on disk and a stale WAL can never be replayed into the new database.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            DeleteSideFiles(path);
            await LocalSchemaMigrator.MigrateAsync(db, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            // Last resort: delete the file if EF cannot open a corrupted store.
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(_options.DbPath))
                {
                    File.Delete(_options.DbPath);
                }

                DeleteSideFiles(_options.DbPath);

                await MigrateAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static string? FilePath(LocalDbContext db)
    {
        var source = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        return string.IsNullOrWhiteSpace(source) || source == ":memory:" ? null : Path.GetFullPath(source);
    }

    private static void DeleteSideFiles(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        foreach (var file in LocalDbConnection.SideFiles(path))
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
