using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Helpers;

/// <summary>
/// Reloads a visible screen after a background sync changed local rows (<see cref="LocalDataVersion.ChangedBySync"/>).
/// Attach when the page appears, Detach when it disappears or the session clears. Bursts (one event per pulled page)
/// merge into one reload <see cref="Delay"/> after the last event. The event arrives on a background thread; the
/// reload runs on the context captured at <see cref="Attach"/> (the UI thread in MAUI).
/// </summary>
public sealed class LiveListRefresh
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(400);

    private readonly Func<Task> _reload;
    private readonly object _gate = new();
    private SynchronizationContext? _context;
    private CancellationTokenSource? _pending;
    private bool _attached;

    public LiveListRefresh(Func<Task> reload, TimeSpan? delay = null)
    {
        _reload = reload;
        Delay = delay ?? DefaultDelay;
    }

    public TimeSpan Delay { get; }

    public bool IsAttached
    {
        get { lock (_gate) { return _attached; } }
    }

    public void Attach()
    {
        lock (_gate)
        {
            if (_attached)
            {
                return;
            }

            _context = SynchronizationContext.Current;
            _attached = true;
            LocalDataVersion.ChangedBySync += OnChanged;
        }
    }

    public void Detach()
    {
        lock (_gate)
        {
            if (!_attached)
            {
                return;
            }

            _attached = false;
            LocalDataVersion.ChangedBySync -= OnChanged;
            _pending?.Cancel();
            _pending = null;
            _context = null;
        }
    }

    private void OnChanged(string businessId)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (!_attached)
            {
                return;
            }

            _pending?.Cancel();
            _pending = cts = new CancellationTokenSource();
        }

        _ = FireAsync(cts);
    }

    private async Task FireAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(Delay, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // merged into a later event, or detached
        }

        SynchronizationContext? context;
        lock (_gate)
        {
            if (!_attached || cts.IsCancellationRequested)
            {
                return;
            }

            context = _context;
        }

        if (context is null)
        {
            await RunAsync(cts).ConfigureAwait(false);
        }
        else
        {
            context.Post(_ => _ = RunAsync(cts), null);
        }
    }

    private async Task RunAsync(CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (!_attached || cts.IsCancellationRequested)
            {
                return;
            }
        }

        try
        {
            await _reload();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live refresh failed: {ex.Message}");
        }
    }
}
