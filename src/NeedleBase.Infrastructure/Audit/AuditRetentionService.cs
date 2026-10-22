using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Audit;

/// <summary>
/// Deletes audit events older than Audit:RetentionDays (default 730 = 2 years) once a day, in batches. The only
/// path allowed past the append-only trigger: it sets <c>needlebase.audit_purge</c> for its own transaction.
/// </summary>
public sealed class AuditRetentionService : BackgroundService
{
    public const int DefaultRetentionDays = 730;
    private const int BatchSize = 5000;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuditRetentionService> _logger;

    public AuditRetentionService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AuditRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public static int RetentionDays(IConfiguration configuration) =>
        Math.Clamp(configuration.GetValue("Audit:RetentionDays", DefaultRetentionDays), 30, 36_500); // never below 30 days by mistake

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(false); // let startup/migrations finish
                var purged = await PurgeAsync(_scopeFactory, DateTimeOffset.UtcNow - TimeSpan.FromDays(RetentionDays(_configuration)), stoppingToken)
                    .ConfigureAwait(false);
                if (purged > 0)
                {
                    _logger.LogInformation("Audit retention purged {Count} events", purged);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit retention purge failed; will retry");
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

    /// <summary>Deletes events before <paramref name="cutoffUtc"/> (all businesses), batch by batch. Returns the count.</summary>
    public static async Task<int> PurgeAsync(IServiceScopeFactory scopeFactory, DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var strategy = db.Database.CreateExecutionStrategy();
            var deleted = await strategy.ExecuteAsync(async ct =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync("SET LOCAL needlebase.audit_purge = 'on'", ct).ConfigureAwait(false);
                var n = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM \"AuditEvents\" WHERE \"Id\" IN (SELECT \"Id\" FROM \"AuditEvents\" WHERE \"OccurredAtUtc\" < {cutoffUtc} LIMIT {BatchSize})",
                    ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return n;
            }, cancellationToken).ConfigureAwait(false);
            total += deleted;
            if (deleted < BatchSize)
            {
                return total;
            }
        }
    }
}
