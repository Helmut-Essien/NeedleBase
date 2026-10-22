using System.Collections.Concurrent;

namespace NeedleBase.Infrastructure.Audit;

/// <summary>
/// Per-process limiter for noisy audit events (failed sign-ins, sync pulls, payment views): one row per key per window;
/// the next row written for the key carries how many were folded in. Multi-instance hosts throttle per instance
/// (a few extra rows, never fewer than one per window).
/// </summary>
public sealed class AuditThrottle
{
    private const int MaxKeys = 20_000;
    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.Ordinal);

    private int _pruning;

    private sealed class Slot
    {
        public long WindowStartTicks = DateTimeOffset.MinValue.UtcTicks;
        public int Suppressed;
    }

    /// <summary>True when an event should be written now; <paramref name="folded"/> = events skipped since the last one.</summary>
    public bool TryEnter(string key, TimeSpan window, DateTimeOffset now, out int folded)
    {
        if (_slots.Count > MaxKeys && Interlocked.Exchange(ref _pruning, 1) == 0)
        {
            try
            {
                // Drop idle keys; under a flood of distinct keys (many emails/IPs) start over rather than grow
                // without bound. Losing a count only means one extra row, never a missing one.
                foreach (var (k, v) in _slots)
                {
                    if (now.UtcTicks - Volatile.Read(ref v.WindowStartTicks) > TimeSpan.FromHours(2).Ticks)
                    {
                        _slots.TryRemove(k, out _);
                    }
                }

                if (_slots.Count > MaxKeys)
                {
                    _slots.Clear();
                }
            }
            finally
            {
                Volatile.Write(ref _pruning, 0);
            }
        }

        var slot = _slots.GetOrAdd(key, _ => new Slot());
        lock (slot)
        {
            if (now.UtcTicks - slot.WindowStartTicks >= window.Ticks)
            {
                folded = slot.Suppressed;
                slot.WindowStartTicks = now.UtcTicks;
                slot.Suppressed = 0;
                return true;
            }

            slot.Suppressed++;
            folded = 0;
            return false;
        }
    }
}
