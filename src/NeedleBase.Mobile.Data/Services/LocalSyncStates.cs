using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Common;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Services;

/// <summary>Queued sync state per row for the list badges: one indexed query for the page of ids shown.</summary>
internal static class LocalSyncStates
{
    public static async Task<Dictionary<string, string>> ForAsync(
        LocalDbContext db, string businessId, string entityType, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return map;
        }

        var idList = ids.ToList();
        var rows = await db.SyncQueue.AsNoTracking()
            .Where(q => q.BusinessId == businessId && q.EntityType == entityType && idList.Contains(q.EntityId))
            .Select(q => new { q.EntityId, q.State })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in rows)
        {
            map.TryGetValue(row.EntityId, out var current);
            map[row.EntityId] = LocalSyncBadge.Combine(current, row.State)!;
        }

        return map;
    }
}
