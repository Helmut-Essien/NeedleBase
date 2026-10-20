namespace NeedleBase.Mobile.Data.Data;

/// <summary>
/// One pending change waiting to be pushed (mobile-only). At most one item per entity:
/// later edits are coalesced into it (see <see cref="LocalDbContext"/>).
/// </summary>
public sealed class SyncQueueItem
{
    /// <summary>ULID; push order (creates before their children).</summary>
    public string Id { get; set; } = string.Empty;
    public string BusinessId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Customer id for measurements/orders (cleanup when the customer is deleted).</summary>
    public string? ParentId { get; set; }

    /// <summary><c>upsert</c> or <c>delete</c> (Application.Contracts.Sync.SyncOperation).</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>JSON of the Sync*Dto to push (null for deletes).</summary>
    public string? PayloadJson { get; set; }

    /// <summary>Server DateModifiedUtc the device last saw; null = never synced (create).</summary>
    public DateTimeOffset? BaseModifiedUtc { get; set; }

    /// <summary>Short user-facing label for Sync details (name or order ref).</summary>
    public string Label { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
    public int RetryCount { get; set; }

    /// <summary><see cref="SyncQueueState"/>.</summary>
    public string State { get; set; } = SyncQueueState.Pending;

    /// <summary>Plain-language reason for a failure or conflict.</summary>
    public string? LastError { get; set; }

    /// <summary>Server copy at conflict time (JSON); null when deleted on another device.</summary>
    public string? ConflictServerJson { get; set; }

    /// <summary>Server code for the conflict/failure (SyncErrorCodes or a rule code); drives the resolve actions.</summary>
    public string? ConflictCode { get; set; }
}

public static class SyncQueueState
{
    /// <summary>Waiting to sync.</summary>
    public const string Pending = "pending";

    /// <summary>Server rejected it (rule); needs Retry or a fix.</summary>
    public const string Failed = "failed";

    /// <summary>Changed or deleted elsewhere; needs the user's choice.</summary>
    public const string Conflict = "conflict";
}

/// <summary>Per-business sync bookkeeping on this device (mobile-only).</summary>
public sealed class DeviceInfo
{
    public string BusinessId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Opaque pull cursor from the server; null = pull everything.</summary>
    public string? PullCursor { get; set; }

    public DateTimeOffset? LastSyncUtc { get; set; }

    /// <summary>
    /// A full pull from scratch is owed (cursor rejected/expired, or a discard/restore needs server copies).
    /// Rows the server no longer has are pruned when it completes. Survives app kills: the run restarts.
    /// </summary>
    public bool ResyncPending { get; set; }

    /// <summary>The pending resync follows a server restore: re-upload what the server lacks instead of pruning it (v6).</summary>
    public bool ResyncRecover { get; set; }
}

/// <summary>
/// A pulled measurement/order whose customer isn't on this device yet (e.g. this device has a pending
/// delete of that customer). Kept, not dropped, and applied once the customer arrives.
/// </summary>
public sealed class SyncDeferredItem
{
    public string Id { get; set; } = string.Empty;
    public string BusinessId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
