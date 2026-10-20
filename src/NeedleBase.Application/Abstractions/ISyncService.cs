using NeedleBase.Application.Contracts.Sync;

namespace NeedleBase.Application.Abstractions;

public interface ISyncService
{
    Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default);

    /// <summary>Null when the cursor is invalid (→ 400 SYNC_CURSOR_INVALID).</summary>
    Task<SyncPullResponse?> PullAsync(SyncPullRequest request, CancellationToken cancellationToken = default);
}
