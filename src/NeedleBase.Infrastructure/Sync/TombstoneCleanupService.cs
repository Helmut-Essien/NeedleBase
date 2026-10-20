using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeedleBase.Application.Sync;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Sync;

/// <summary>
/// Purges sync tombstones older than <see cref="SyncTime.TombstoneRetention"/> once a day. Pull refuses
/// cursors older than the same window (400 SYNC_CURSOR_INVALID), so such a device resyncs from scratch
/// instead of missing deletes.
/// </summary>
public sealed class TombstoneCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TombstoneCleanupService> _logger;

    public TombstoneCleanupService(IServiceScopeFactory scopeFactory, ILogger<TombstoneCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(_scopeFactory, DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Tombstone cleanup failed; will retry");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public static async Task<int> PurgeAsync(IServiceScopeFactory scopeFactory, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var threshold = utcNow - SyncTime.TombstoneRetention;
        // Cross-tenant maintenance by design (no business context); deletes only expired tombstones.
        return await db.DeletedItems.IgnoreQueryFilters()
            .Where(d => d.DeletedAtUtc < threshold)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
