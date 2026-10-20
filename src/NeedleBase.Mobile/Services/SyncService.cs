using NeedleBase.Mobile.Data.Sync;

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

    public bool IsOnline => _connectivity.NetworkAccess == NetworkAccess.Internet;

    /// <summary>True while a transient-failure backoff is running (a local save won't start a run early).</summary>
    public bool IsBackingOff => _now() < _backoffUntil;

    /// <summary>Hook triggers and run once. Safe to call repeatedly (login after logout or re-sign-in).</summary>
    public void Start()
    {
        if (!_started)
        {
            _started = true;
            _connectivity.ConnectivityChanged += OnConnectivityChanged;
            LocalSyncSignals.QueueChanged += OnQueueChanged;
        }

        ResetFailures();
        Trigger();
    }

    /// <summary>Stop scheduled work (sign out).</summary>
    public void Stop()
    {
        _delayCts?.Cancel();
        ResetFailures();
        _lastBusinessId = null;
        Publish(new SyncQueueSummary(0, 0, 0, null), SyncChipState.Synced);
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

    public void Trigger() => _ = RunAsync();

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
        lock (_gate)
        {
            if (_running)
            {
                _rerun = true;
                return;
            }

            _running = true;
            _rerun = false;
        }

        while (true)
        {
            await RunOnceAsync().ConfigureAwait(false);
            lock (_gate)
            {
                if (!_rerun)
                {
                    _running = false;
                    return;
                }

                _rerun = false;
            }
        }
    }

    private async Task RunOnceAsync()
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
            await _engine.RunAsync(businessId).ConfigureAwait(false);
            _failures = 0;
            _backoffUntil = DateTimeOffset.MinValue;
            _lastError = null;
        }
        catch (SyncSessionExpiredException)
        {
            // Keep the queue and the local data; signing in again (same account) continues where it left off.
            _sessionExpired = true;
            _lastError = "Sign in again to sync. Your changes are kept on this device.";
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
            _lastError = "Something went wrong while syncing.";
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
