using System.Collections.Concurrent;
using NeedleBase.Application.Abstractions;

namespace NeedleBase.Infrastructure.Identity;

/// <summary>
/// Replaces the account-wide Identity lockout (which let anyone lock any owner out by typing their email wrong
/// five times). Failures are counted per email + client IP: after <see cref="FreeFailures"/> wrong passwords that
/// pair must wait, doubling from <see cref="FirstDelay"/> up to <see cref="MaxDelay"/>. The real owner on another
/// network is never blocked; an attacker on one IP gets ~14 tries/hour. The "auth" per-IP rate limit still caps
/// guesses across many emails. In memory (one API instance); a restart forgets counts, which only helps the user.
/// </summary>
public sealed class LoginThrottle
{
    public const int FreeFailures = 5;
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(15);

    /// <summary>A pair with no failure for this long starts over.</summary>
    public static readonly TimeSpan Forget = TimeSpan.FromHours(1);

    private const int MaxEntries = 100_000;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly IClock _clock;

    public LoginThrottle(IClock clock)
    {
        _clock = clock;
    }

    /// <summary>How long this email + IP must still wait, or null when it may try now.</summary>
    public TimeSpan? RetryAfter(string email, string? ip)
    {
        if (!_entries.TryGetValue(Key(email, ip), out var entry))
        {
            return null;
        }

        lock (entry)
        {
            var now = _clock.UtcNow;
            if (now - entry.LastFailureUtc >= Forget)
            {
                return null;
            }

            return entry.BlockedUntilUtc > now ? entry.BlockedUntilUtc - now : null;
        }
    }

    public void RecordFailure(string email, string? ip)
    {
        var now = _clock.UtcNow;
        if (_entries.Count >= MaxEntries)
        {
            Prune(now);
        }

        var entry = _entries.GetOrAdd(Key(email, ip), _ => new Entry());
        lock (entry)
        {
            if (now - entry.LastFailureUtc >= Forget)
            {
                entry.Failures = 0;
            }

            entry.Failures++;
            entry.LastFailureUtc = now;
            if (entry.Failures >= FreeFailures)
            {
                var steps = Math.Min(entry.Failures - FreeFailures, 10);
                var delay = TimeSpan.FromTicks(Math.Min(FirstDelay.Ticks << steps, MaxDelay.Ticks));
                entry.BlockedUntilUtc = now + delay;
            }
        }
    }

    public void RecordSuccess(string email, string? ip) => _entries.TryRemove(Key(email, ip), out _);

    private void Prune(DateTimeOffset now)
    {
        foreach (var pair in _entries)
        {
            if (now - pair.Value.LastFailureUtc >= Forget)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private static string Key(string email, string? ip) =>
        email.Trim().ToUpperInvariant() + "|" + (ip ?? "unknown");

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset LastFailureUtc = DateTimeOffset.MinValue;
        public DateTimeOffset BlockedUntilUtc = DateTimeOffset.MinValue;
    }
}
