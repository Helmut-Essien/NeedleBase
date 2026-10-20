namespace NeedleBase.Mobile.Helpers;

/// <summary>
/// Search-as-you-type: each <see cref="Run"/> cancels the previous pending/in-flight query,
/// waits <see cref="Delay"/>, then runs the action with a token that is cancelled when a newer
/// keystroke arrives. Await continuations resume on the caller's context (UI thread in MAUI).
/// </summary>
public sealed class SearchDebouncer
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(250);

    private CancellationTokenSource? _current;

    public SearchDebouncer(TimeSpan? delay = null) => Delay = delay ?? DefaultDelay;

    public TimeSpan Delay { get; }

    public Task Run(Func<CancellationToken, Task> action)
    {
        var next = new CancellationTokenSource();
        Interlocked.Exchange(ref _current, next)?.Cancel();
        return RunCoreAsync(action, next.Token);
    }

    /// <summary>Cancel pending and in-flight work (e.g. logout, leaving the page).</summary>
    public void Cancel() => Interlocked.Exchange(ref _current, null)?.Cancel();

    private async Task RunCoreAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        try
        {
            await Task.Delay(Delay, token);
            await action(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded by a newer keystroke.
        }
    }
}
