namespace NeedleBase.Application.Sync;

/// <summary>
/// Sync timestamps are compared at microsecond precision (PostgreSQL timestamptz). Always
/// normalise before comparing or returning so a value read back from the database equals the
/// value the device was given.
/// </summary>
public static class SyncTime
{
    /// <summary>Shadow column on synced server tables: when the row last changed (server clock). Informational.</summary>
    public const string ModifiedColumn = "SyncModifiedUtc";

    /// <summary>
    /// Shadow column on synced server tables + DeletedItems: id of the transaction that last wrote the row,
    /// set by a database trigger (<c>pg_current_xact_id()</c>). Pull pages by this, capped below the
    /// snapshot xmin, so a row can never commit "behind" a cursor (commit-ordered, no overlap window).
    /// </summary>
    public const string TxColumn = "SyncTxId";

    /// <summary>Tombstones older than this are purged; a cursor older than this must resync from scratch.</summary>
    public static readonly TimeSpan TombstoneRetention = TimeSpan.FromDays(90);

    public static DateTimeOffset Normalize(DateTimeOffset value)
    {
        var ticks = value.UtcTicks;
        return new DateTimeOffset(ticks - (ticks % 10), TimeSpan.Zero);
    }

    public static bool SameInstant(DateTimeOffset a, DateTimeOffset b) =>
        Normalize(a).UtcTicks == Normalize(b).UtcTicks;
}
