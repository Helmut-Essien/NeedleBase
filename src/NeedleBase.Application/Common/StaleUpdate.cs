using NeedleBase.Application.Sync;

namespace NeedleBase.Application.Common;

/// <summary>
/// Optional optimistic concurrency for REST updates: a client that sends the DateModifiedUtc it last saw gets
/// 409 <see cref="Code"/> instead of silently overwriting someone else's newer change. Omitting it keeps the
/// old last-write-wins behaviour (older clients, sync, which has its own base check).
/// </summary>
public static class StaleUpdate
{
    public const string Code = "STALE_UPDATE";

    /// <summary>Default copy for <see cref="RecordError.Stale"/> / <c>OrderError.Stale</c>; callers branch on the error, not this text.</summary>
    public const string Message = "Changed by someone else since you opened it.";

    public static bool IsStale(DateTimeOffset? baseModifiedUtc, DateTimeOffset currentModifiedUtc) =>
        baseModifiedUtc is { } b && !SyncTime.SameInstant(b, currentModifiedUtc);
}
