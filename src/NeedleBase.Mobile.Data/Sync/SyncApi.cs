using NeedleBase.Application.Contracts.Sync;

namespace NeedleBase.Mobile.Data.Sync;

/// <summary>Transport for sync (HTTP in the app, fakes in tests).</summary>
public interface ISyncApi
{
    Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken);
    Task<SyncPullResponse> PullAsync(SyncPullRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Network/server trouble (offline, timeout, 5xx/429, refresh failed transiently). Retry later; keep the queue.
/// <see cref="RetryAfter"/> is the server's Retry-After, when it sent one.
/// </summary>
public sealed class SyncTransientException(string message, Exception? inner = null, TimeSpan? retryAfter = null, bool rateLimited = false)
    : Exception(message, inner)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>429: the request wasn't processed; the engine waits Retry-After and carries on within the run.</summary>
    public bool RateLimited { get; } = rateLimited;
}

/// <summary>The session is no longer valid (refresh rejected). Keep the queue; the user must sign in.</summary>
public sealed class SyncSessionExpiredException() : Exception("Session expired.");

/// <summary>403: this account may not sync this business (removed or role changed). Not retried automatically.</summary>
public sealed class SyncAccessDeniedException() : Exception("No access to sync for this business.");

/// <summary>A 400 other than a bad cursor: the server can't read what this app sends (usually an outdated app). Not retried automatically.</summary>
public sealed class SyncRejectedException(string? code) : Exception("The server rejected the sync request.")
{
    public string? Code { get; } = code;
}

/// <summary>The server rejected the stored pull cursor (unreadable or older than tombstone retention): resync from scratch.</summary>
public sealed class SyncCursorInvalidException(bool serverReset = false) : Exception("Sync cursor rejected.")
{
    /// <summary>
    /// The server's data was restored or moved (SYNC_EPOCH_CHANGED): records it no longer has are uploaded
    /// again from this device instead of being deleted here.
    /// </summary>
    public bool ServerReset { get; } = serverReset;
}
