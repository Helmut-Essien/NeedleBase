using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Common;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.WhatsApp;

/// <summary>
/// Adds an outbox row for each order whose status changed to one the business sends WhatsApp messages for, in the
/// same SaveChanges as the change (REST, web and sync push alike), so it commits or rolls back with it.
/// Costs nothing unless an order status changed; then 1 query (settings), plus 2 when a business has it on.
/// One row per order + status while one is pending or sent (sync replays and quick flips don't duplicate).
/// </summary>
internal static class WhatsAppOutbox
{
    private static readonly string[] Blocking =
    [
        WhatsAppMessageState.Pending, WhatsAppMessageState.Sending, WhatsAppMessageState.Sent,
        WhatsAppMessageState.Delivered, WhatsAppMessageState.Read
    ];

    public static async Task AddAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        // Rows still "Added" belong to an earlier SaveChanges that failed and is being retried: rebuild them.
        foreach (var stale in db.ChangeTracker.Entries<WhatsAppMessage>().Where(e => e.State == EntityState.Added).ToList())
        {
            stale.State = EntityState.Detached;
        }

        List<(string BusinessId, string OrderId, string CustomerId, string Status)>? changed = null;
        foreach (var entry in db.ChangeTracker.Entries<Order>())
        {
            var status = entry.Entity.Status;
            var isChange = entry.State switch
            {
                EntityState.Added => status != OrderStatus.New,
                EntityState.Modified => entry.Property(o => o.Status).IsModified &&
                                        !string.Equals(entry.Property(o => o.Status).OriginalValue, status, StringComparison.Ordinal),
                _ => false
            };
            if (isChange)
            {
                (changed ??= []).Add((entry.Entity.BusinessId, entry.Entity.Id, entry.Entity.CustomerId, status));
            }
        }

        if (changed is null)
        {
            return;
        }

        var businessIds = changed.Select(c => c.BusinessId).Distinct().ToList();
        var settings = await db.WhatsAppSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(s => businessIds.Contains(s.BusinessId) && s.Enabled)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (settings.Count == 0)
        {
            return;
        }

        var wanted = changed.Where(c => settings.Any(s => s.BusinessId == c.BusinessId && s.Sends(c.Status))).ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        var customerIds = wanted.Select(c => c.CustomerId).Distinct().ToList();
        var consenting = await db.WhatsAppConsents.IgnoreQueryFilters().AsNoTracking()
            .Where(c => customerIds.Contains(c.CustomerId) && c.OptedIn)
            .Select(c => c.CustomerId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        wanted = wanted.Where(c => consenting.Contains(c.CustomerId)).ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        var orderIds = wanted.Select(c => c.OrderId).Distinct().ToList();
        var existing = await db.WhatsAppMessages.IgnoreQueryFilters().AsNoTracking()
            .Where(m => orderIds.Contains(m.OrderId) && Blocking.Contains(m.State))
            .Select(m => new { m.OrderId, m.OrderStatus })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        foreach (var c in wanted.DistinctBy(c => (c.OrderId, c.Status)))
        {
            if (existing.Any(e => e.OrderId == c.OrderId && e.OrderStatus == c.Status))
            {
                continue;
            }

            db.WhatsAppMessages.Add(WhatsAppMessage.Create(IdGenerator.NewId(), c.BusinessId, c.OrderId, c.CustomerId, c.Status, now, TimeSpan.Zero));
        }
    }
}
