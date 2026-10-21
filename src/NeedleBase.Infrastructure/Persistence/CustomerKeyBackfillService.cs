using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeedleBase.Application.Common;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Infrastructure.Persistence;

/// <summary>
/// One-off data step for rows saved before the search keys existed (SearchName null): stamps the keys and
/// normalises the phone (<see cref="PhoneNumbers.Normalize"/>), in batches, across all businesses. Written with
/// ExecuteUpdate so DateModifiedUtc is untouched (not a user edit, so a device's queued edit is not made stale);
/// the SyncTxId trigger still restamps the row, so devices pull the normalised phone. Idempotent; retries until
/// the migration that adds the columns has run.
/// </summary>
public sealed class CustomerKeyBackfillService : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CustomerKeyBackfillService> _logger;

    public CustomerKeyBackfillService(IServiceScopeFactory scopeFactory, ILogger<CustomerKeyBackfillService> logger)
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
                var count = await RunAsync(_scopeFactory, stoppingToken).ConfigureAwait(false);
                if (count > 0)
                {
                    _logger.LogInformation("Backfilled search keys for {Count} customers", count);
                }

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Customer search-key backfill failed; will retry");
            }

            try
            {
                await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public static async Task<int> RunAsync(IServiceScopeFactory scopeFactory, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Cross-tenant maintenance by design (no business context).
            var batch = await db.Customers.IgnoreQueryFilters().AsNoTracking()
                .Where(c => EF.Property<string?>(c, CustomerSearchKeys.SearchNameColumn) == null)
                .OrderBy(c => c.Id)
                .Take(BatchSize)
                .Select(c => new { c.Id, c.Name, c.Phone })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return total;
            }

            foreach (var row in batch)
            {
                var phone = PhoneNumbers.Normalize(row.Phone);
                if (phone.Length == 0 || phone.Length > FieldLimits.Phone)
                {
                    phone = row.Phone;
                }

                var name = CustomerSearchKeys.NameKey(row.Name);
                var digits = CustomerSearchKeys.PhoneDigits(phone);
                await db.Customers.IgnoreQueryFilters()
                    .Where(c => c.Id == row.Id)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(c => c.Phone, phone)
                            .SetProperty(c => EF.Property<string?>(c, CustomerSearchKeys.SearchNameColumn), name)
                            .SetProperty(c => EF.Property<string?>(c, CustomerSearchKeys.PhoneDigitsColumn), digits),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            total += batch.Count;
        }
    }
}
