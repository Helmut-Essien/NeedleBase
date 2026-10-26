using System.Collections.Concurrent;
using NeedleBase.Application.Abstractions;

namespace NeedleBase.Infrastructure.Identity;

/// <summary>
/// Loose cap on reset requests from one IP across all addresses (stops one machine mailing many people).
/// High enough for a carrier IP shared by many Ghana phones (CGNAT): real shops make a handful an hour.
/// Fixed one-hour window, in memory, bounded.
/// </summary>
public sealed class ResetRequestIpCap
{
    public const int PerHour = 60;
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private const int MaxEntries = 50_000;

    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly IClock _clock;

    public ResetRequestIpCap(IClock clock) => _clock = clock;

    /// <summary>Counts this request; returns the wait when the IP is over the cap.</summary>
    public TimeSpan? TryTake(string? ip)
    {
        var now = _clock.UtcNow;
        if (_counters.Count >= MaxEntries)
        {
            foreach (var pair in _counters)
            {
                if (now - pair.Value.StartUtc >= Window)
                {
                    _counters.TryRemove(pair.Key, out _);
                }
            }
        }

        var counter = _counters.GetOrAdd(string.IsNullOrWhiteSpace(ip) ? "unknown" : ip, _ => new Counter { StartUtc = now });
        lock (counter)
        {
            if (now - counter.StartUtc >= Window)
            {
                counter.StartUtc = now;
                counter.Count = 0;
            }

            if (counter.Count >= PerHour)
            {
                return counter.StartUtc + Window - now;
            }

            counter.Count++;
            return null;
        }
    }

    private sealed class Counter
    {
        public DateTimeOffset StartUtc;
        public int Count;
    }
}
