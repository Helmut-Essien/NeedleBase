using NeedleBase.Application.Contracts.Sync;

namespace NeedleBase.Application.Abstractions;

public interface ISyncService
{
    Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default);

    /// <summary>Null when the cursor is invalid (→ 400 SYNC_CURSOR_INVALID).</summary>
    /// <summary>A page of changes, or an error code (<see cref="SyncErrorCodes.CursorInvalid"/> / <see cref="SyncErrorCodes.EpochChanged"/>).</summary>
    Task<(SyncPullResponse? Page, string? ErrorCode)> PullAsync(SyncPullRequest request, CancellationToken cancellationToken = default);
}
