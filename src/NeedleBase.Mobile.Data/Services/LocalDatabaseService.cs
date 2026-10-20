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
