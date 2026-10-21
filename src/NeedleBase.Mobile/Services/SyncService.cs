using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Data.Services;

namespace NeedleBase.Mobile.Services;

public enum SyncChipState
{
    Synced,
    Waiting,
    Syncing,
    Offline,
    Failed,
    NeedsReview,
    SignInNeeded,
    NoAccess,
    Blocked
}

public sealed record SyncStatusSnapshot(
    SyncChipState State,
    int Pending,
    int NeedsAttention,
    DateTimeOffset? LastSyncUtc,
    string? LastError);

/// <summary>
/// App-wide sync orchestrator (singleton). Runs push-then-pull via <see cref="SyncEngine"/> on: main shown
/// (app start / login), connectivity regained, after a local save (debounced), and manual "Sync now".
/// One run at a time; a trigger during a run schedules exactly one more. Transient failures back off
/// 5s/15s/60s/5min (or the server's Retry-After, if longer); a local save during a backoff does not cut it short.
/// 403 and non-cursor 400 are not retried automatically (own chip states); Sync now or the next app start retries.
/// </summary>
public sealed class SyncService
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5)];

    private static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(2);

    /// <summary>While the app is in the foreground, pull other devices' changes at least this often.</summary>
    public static readonly TimeSpan ForegroundInterval = TimeSpan.FromMinutes(10);

    /// <summary>Pull-to-refresh waits at most this long for sync before showing what is on the device.</summary>
    public static readonly TimeSpan RefreshWait = TimeSpan.FromSeconds(15);

    private CancellationTokenSource? _foregroundCts;
    private DateTimeOffset _lastForegroundRun = DateTimeOffset.MinValue;
    private static readonly TimeSpan ForegroundThrottle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    private readonly SyncEngine _engine;
    private readonly IAuthService _auth;
    private readonly IConnectivity _connectivity;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private CancellationTokenSource? _delayCts;
    private int _failures;
    private bool _running;
    private bool _rerun;
    private bool _started;
    private string? _lastError;
    private string? _lastBusinessId;
    private DateTimeOffset _backoffUntil = DateTimeOffset.MinValue;
    private SyncChipState? _blockedState;
    private bool _sessionExpired;
    private bool _stopped;
    private CancellationTokenSource? _runCts;
    private TaskCompletionSource? _idle;

    public SyncService(SyncEngine engine, IAuthService auth, IConnectivity connectivity)
        : this(engine, auth, connectivity, () => DateTimeOffset.UtcNow)
    {
    }

    public SyncService(SyncEngine engine, IAuthService auth, IConnectivity connectivity, Func<DateTimeOffset> now)
    {
        _engine = engine;
        _auth = auth;
        _connectivity = connectivity;
        _now = now;
        Current = new SyncStatusSnapshot(SyncChipState.Synced, 0, 0, null, null);
    }

    public SyncStatusSnapshot Current { get; private set; }

    public SyncEngine Engine => _engine;

    public event EventHandler<SyncStatusSnapshot>? StatusChanged;

    /// <summary>Raised when sync lost its session because the server revoked access (see AccessRevokedService).</summary>
    public event EventHandler? AccessRevoked;

    public bool IsOnline => _connectivity.NetworkAccess == NetworkAccess.Internet;

    /// <summary>True while a transient-failure backoff is running (a local save won't start a run early).</summary>
    public bool IsBackingOff => _now() < _backoffUntil;

    /// <summary>Hook triggers and run once. Safe to call repeatedly (login after logout or re-sign-in).</summary>
    public void Start()
    {
        lock (_gate)
        {
            _stopped = false;
        }

        if (!_started)
        {
            _started = true;
            _connectivity.ConnectivityChanged += OnConnectivityChanged;
            LocalSyncSignals.QueueChanged += OnQueueChanged;
        }

        ResetFailures();
        Trigger();
    }

    /// <summary>
    /// Sign-out step 1: block new runs, cancel the scheduled one and the one in flight, and wait until it has
    /// finished, so nothing writes to the database after it is wiped. The wait happens outside the gate lock
    /// (the run loop only takes the lock briefly), so it can't deadlock. <see cref="Start"/> unblocks.
    /// </summary>
    public async Task StopAsync()
    {
        Task idle;
        lock (_gate)
        {
            _stopped = true;
            _delayCts?.Cancel();
            _runCts?.Cancel();
            idle = _running && _idle is not null ? _idle.Task : Task.CompletedTask;
        }

        await idle.ConfigureAwait(false);
        ResetFailures();
        _lastBusinessId = null;
        Publish(new SyncQueueSummary(0, 0, 0, null), SyncChipState.Synced);
    }

    /// <summary>True between <see cref="StopAsync"/> and the next <see cref="Start"/>.</summary>
    public bool IsStopped
    {
        get
        {
            lock (_gate)
            {
                return _stopped;
            }
        }
    }

    /// <summary>User tapped Sync now / Retry: failed items go back to the queue and a run starts immediately.</summary>
    public async Task SyncNowAsync()
    {
        var businessId = await _auth.GetBusinessIdAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(businessId))
        {
            await _engine.RetryFailedAsync(businessId).ConfigureAwait(false);
        }

        ResetFailures();
        await RunAsync().ConfigureAwait(false);
    }

    public async Task<bool> ResolveAsync(string itemId, bool keepMine)
    {
        var businessId = await _auth.GetBusinessIdAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(businessId))
        {
            return false;
        }

        var done = await _engine.ResolveConflictAsync(businessId, itemId, keepMine).ConfigureAwait(false);
        Trigger();
        return done;
    }

    public async Task DiscardAsync(string itemId)
    {
        var businessId = await _auth.GetBusinessIdAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(businessId))
        {
            return;
        }

        await _engine.DiscardAsync(businessId, itemId).ConfigureAwait(false);
        Trigger();
    }

    public async Task<DiscardImpact> GetDiscardImpactAsync(string itemId)
    {
        var businessId = await _auth.GetBusinessIdAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(businessId)
            ? new DiscardImpact(false, 0, 0)
            : await _engine.GetDiscardImpactAsync(businessId, itemId).ConfigureAwait(false);
    }

    public async Task<int> PendingCountAsync()
    {
        var businessId = await _auth.GetBusinessIdAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(businessId))
        {
            return 0;
        }

        var summary = await _engine.GetSummaryAsync(businessId).ConfigureAwait(false);
        return summary.Pending + summary.NeedsAttention;
    }

    /// <summary>Unsynced changes (queued or needing attention) for a business, even after the tokens were cleared.</summary>
    public async Task<int> PendingCountForAsync(string businessId)
    {
        var summary = await _engine.GetSummaryAsync(businessId).ConfigureAwait(false);
        return summary.Pending + summary.NeedsAttention;
    }

    public void Trigger() => _ = RunAsync();

    /// <summary>
    /// App came to the foreground (resume / first window): sync now, unless a backoff or a blocking state is
    /// already deciding when to retry, and keep syncing every <see cref="ForegroundInterval"/> while visible.
    /// </summary>
    public void OnForeground()
    {
        var now = _now();
        if (now - _lastForegroundRun >= ForegroundThrottle &&
            !IsStopped && _started && !IsBackingOff && _blockedState is null && !_sessionExpired)
        {
            _lastForegroundRun = now; // a dialog or picker closing also "activates": don't sync every time
            Trigger();
        }

        _foregroundCts?.Cancel();
        var cts = new CancellationTokenSource();
        _foregroundCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(ForegroundInterval, cts.Token).ConfigureAwait(false);
                    if (_started && !IsStopped && !IsBackingOff && _blockedState is null && !_sessionExpired)
                    {
                        await RunAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <summary>App went to the background: stop the foreground timer (no battery use while hidden).</summary>
    public void OnBackground()
    {
        _foregroundCts?.Cancel();
        _foregroundCts = null;
    }

    /// <summary>Pull-to-refresh: sync now when possible, waiting at most <see cref="RefreshWait"/>.</summary>
    public async Task SyncForRefreshAsync()
    {
        if (IsStopped || !_started || !IsOnline || _sessionExpired || _blockedState is not null)
        {
            return;
        }

        try
        {
            var run = SyncNowAsync();
            await Task.WhenAny(run, Task.Delay(RefreshWait)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Refresh sync failed: {ex.Message}");
        }
    }

    private void ResetFailures()
    {
        _failures = 0;
        _backoffUntil = DateTimeOffset.MinValue;
        _blockedState = null;
        _sessionExpired = false;
        _lastError = null;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet && _blockedState is null)
        {
            // Back online: a fresh network deserves a fresh try.
            _failures = 0;
            _backoffUntil = DateTimeOffset.MinValue;
            Trigger();
        }
        else
        {
            _ = RefreshStatusAsync();
        }
    }

    public void OnQueueChanged(object? sender, EventArgs e)
    {
        _ = RefreshStatusAsync();
        if (IsBackingOff || _blockedState is not null || _sessionExpired)
        {
            return; // the pending backoff (or the user) decides when to try again
        }

        Schedule(SaveDebounce);
    }

    private void Schedule(TimeSpan delay)
    {
        _delayCts?.Cancel();
        var cts = new CancellationTokenSource();
        _delayCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                await RunAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    public async Task RunAsync()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            if (_running)
            {
                _rerun = true;
                return;
            }

            _running = true;
            _rerun = false;
            _runCts = new CancellationTokenSource();
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token = _runCts.Token;
        }

        try
        {
            while (true)
            {
                await RunOnceAsync(token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (!_rerun || _stopped)
                    {
                        return;
                    }

                    _rerun = false;
                }
            }
        }
        finally
        {
            TaskCompletionSource? idle;
            lock (_gate)
            {
                _running = false;
                _runCts?.Dispose();
                _runCts = null;
                idle = _idle;
            }

            idle?.TrySetResult();
        }
    }

    private async Task RunOnceAsync(CancellationToken token)
    {
        string? businessId;
        try
        {
            businessId = await _auth.GetBusinessIdAsync().ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(businessId))
        {
            // Signed out, or the session expired (tokens cleared, local data kept): show that honestly.
            if (_sessionExpired && _lastBusinessId is not null)
            {
                await PublishAsync(_lastBusinessId, SyncChipState.SignInNeeded).ConfigureAwait(false);
            }

            return;
        }

        _lastBusinessId = businessId;
        if (!IsOnline || _sessionExpired || _blockedState is not null)
        {
            await RefreshStatusAsync().ConfigureAwait(false);
            return;
        }

        await PublishAsync(businessId, SyncChipState.Syncing).ConfigureAwait(false);
        try
        {
            await _engine.RunAsync(businessId, token).ConfigureAwait(false);
            _failures = 0;
            _backoffUntil = DateTimeOffset.MinValue;
            _lastError = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return; // stopped (sign-out): the next session starts fresh
        }
        catch (SyncSessionExpiredException)
        {
            // Keep the queue and the local data; signing in again (same account) continues where it left off.
            _sessionExpired = true;
            _lastError = "Sign in again to sync. Your changes are kept on this device.";
            if (await _auth.GetAccessRevokedAsync().ConfigureAwait(false) is not null)
            {
                AccessRevoked?.Invoke(this, EventArgs.Empty); // deactivated / removed: the device is wiped instead
            }
        }
        catch (SyncAccessDeniedException)
        {
            _blockedState = SyncChipState.NoAccess;
            _lastError = "This account can't sync this business any more. Ask the owner to check your access.";
        }
        catch (SyncRejectedException)
        {
            _blockedState = SyncChipState.Blocked;
            _lastError = "NeedleBase couldn't accept these changes. Update the app, then tap Sync now.";
        }
        catch (Exception ex) when (ex is SyncTransientException or HttpRequestException or TaskCanceledException
                                       or SyncCursorInvalidException)
        {
            _lastError = ex.Message;
            BackOff((ex as SyncTransientException)?.RetryAfter);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sync failed: {ex}");
            _lastError = LocalStorageErrors.Describe(ex) ?? "Something went wrong while syncing.";
            BackOff(null);
        }

        await PublishAsync(businessId, null).ConfigureAwait(false);
    }

    private void BackOff(TimeSpan? retryAfter)
    {
        var delay = Backoff[Math.Min(_failures, Backoff.Length - 1)];
        if (retryAfter is { } ra && ra > delay)
        {
            delay = ra > MaxRetryAfter ? MaxRetryAfter : ra;
        }

        _failures++;
        _backoffUntil = _now() + delay;
        Schedule(delay);
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var businessId = await _auth.GetBusinessIdAsync().ConfigureAwait(false) ?? (_sessionExpired ? _lastBusinessId : null);
            if (string.IsNullOrWhiteSpace(businessId))
            {
                return;
            }

            await PublishAsync(businessId, null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sync status failed: {ex.Message}");
        }
    }

    private async Task PublishAsync(string businessId, SyncChipState? forced)
    {
        var summary = await _engine.GetSummaryAsync(businessId).ConfigureAwait(false);
        Publish(summary, forced ?? Derive(summary));
    }

    public SyncChipState Derive(SyncQueueSummary summary)
    {
        if (_sessionExpired) return SyncChipState.SignInNeeded;
        if (_blockedState is { } blocked) return blocked;
        if (summary.NeedsAttention > 0) return SyncChipState.NeedsReview;
        if (!IsOnline) return SyncChipState.Offline;
        if (_lastError is not null) return SyncChipState.Failed;
        if (summary.Pending > 0) return SyncChipState.Waiting;
        return summary.LastSyncUtc is null ? SyncChipState.Waiting : SyncChipState.Synced;
    }

    private void Publish(SyncQueueSummary summary, SyncChipState state)
    {
        Current = new SyncStatusSnapshot(state, summary.Pending, summary.NeedsAttention, summary.LastSyncUtc, _lastError);
        StatusChanged?.Invoke(this, Current);
    }
}
