using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Audit;
using NeedleBase.Domain.Entities;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.WhatsApp;

/// <summary>
/// Meta webhook: GET verification (hub.verify_token) and POST events signed with the app secret
/// (X-Hub-Signature-256: sha256=HMAC(body)). Applies delivery statuses to our messages and treats a "STOP" reply as
/// opt-out for every customer with that number. Unknown or unrelated events are ignored (still 200, so Meta doesn't retry).
/// </summary>
public static class WhatsAppWebhook
{
    public const int MaxBodyBytes = 1_000_000;
    private static readonly string[] StopWords = ["STOP", "STOP ALL", "UNSUBSCRIBE", "OPT OUT", "OPTOUT", "CANCEL MESSAGES"];

    /// <summary>Returns hub.challenge when mode/token match; null otherwise (answer 403).</summary>
    public static string? Verify(WhatsAppOptions options, string? mode, string? token, string? challenge)
    {
        if (string.IsNullOrWhiteSpace(options.VerifyToken) || mode != "subscribe" || string.IsNullOrEmpty(challenge) || token is null)
        {
            return null;
        }

        var a = Encoding.UTF8.GetBytes(token);
        var b = Encoding.UTF8.GetBytes(options.VerifyToken);
        return CryptographicOperations.FixedTimeEquals(a, b) && challenge.Length <= 200 ? challenge : null;
    }

    public static bool SignatureValid(string? appSecret, ReadOnlySpan<byte> body, string? header)
    {
        if (string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] given;
        try
        {
            given = Convert.FromHexString(header.AsSpan(7).Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body);
        return given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected);
    }

    /// <summary>StopAt: when each number sent STOP (Meta's message timestamp; latest per number), so a replayed old
    /// STOP can't undo a newer opt-in.</summary>
    public sealed record Parsed(List<(string Id, string State, string? Error)> Statuses, List<string> StopFrom)
    {
        public Dictionary<string, DateTimeOffset> StopAt { get; init; } = new(StringComparer.Ordinal);
    }

    public static Parsed Parse(ReadOnlySpan<byte> body)
    {
        var statuses = new List<(string, string, string?)>();
        var stops = new List<string>();
        var stopAt = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(body.ToArray());
        if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return new Parsed(statuses, stops);
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (change.ValueKind != JsonValueKind.Object || !change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (value.TryGetProperty("statuses", out var sts) && sts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var st in sts.EnumerateArray())
                    {
                        var id = Str(st, "id");
                        var state = WhatsAppMessageState.FromMeta(Str(st, "status"));
                        if (id is null || state is null)
                        {
                            continue;
                        }

                        string? error = null;
                        if (st.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
                        {
                            var e = errs[0];
                            var code = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("code", out var c) ? c.ToString() : null;
                            error = $"{code} {Str(e, "title") ?? Str(e, "message")}".Trim();
                        }

                        statuses.Add((id, state, error));
                    }
                }

                if (value.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in msgs.EnumerateArray())
                    {
                        var from = Str(m, "from");
                        string? text = null;
                        if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("text", out var t))
                        {
                            text = Str(t, "body");
                        }
                        else if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("button", out var btn))
                        {
                            text = Str(btn, "text"); // template quick-reply "Stop promotions"-style buttons
                        }

                        if (from is not null && from.All(char.IsAsciiDigit) && from.Length is >= 8 and <= 15 && IsStop(text))
                        {
                            stops.Add(from);
                            if (long.TryParse(Str(m, "timestamp"), out var unix) && unix is > 0 and < 32503680000)
                            {
                                var when = DateTimeOffset.FromUnixTimeSeconds(unix);
                                if (!stopAt.TryGetValue(from, out var seen) || when > seen)
                                {
                                    stopAt[from] = when;
                                }
                            }
                        }
                    }
                }
            }
        }

        return new Parsed(statuses, stops) { StopAt = stopAt };
    }

    public static bool IsStop(string? text)
    {
        var t = (text ?? string.Empty).Trim().TrimEnd('.', '!').ToUpperInvariant();
        return StopWords.Contains(t) || t.StartsWith("STOP ", StringComparison.Ordinal);
    }

    /// <summary>Applies a parsed event. Runs without a tenant (IgnoreQueryFilters), matching by Meta message id / phone.</summary>
    public static async Task<(int Updated, int OptedOut)> ApplyAsync(ApplicationDbContext db, IAuditLog audit, Parsed parsed, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var updated = 0;
        var ids = parsed.Statuses.Select(s => s.Id).Distinct().Take(500).ToList();
        if (ids.Count > 0)
        {
            var rows = await db.WhatsAppMessages.IgnoreQueryFilters()
                .Where(m => m.ProviderMessageId != null && ids.Contains(m.ProviderMessageId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var (id, state, error) in parsed.Statuses)
            {
                foreach (var row in rows.Where(r => r.ProviderMessageId == id))
                {
                    if (row.ApplyDelivery(state, error, now))
                    {
                        updated++;
                    }
                }
            }
        }

        // Default is off, so only customers who opted in can be switched off: start from the (small) consent table and
        // join each customer by primary key, instead of scanning every business's customers for the phone.
        var optedOut = 0;
        var stopped = new List<(string BusinessId, string CustomerId, string Phone)>();
        var phones = parsed.StopFrom.Distinct().Take(50).Select(f => "+" + f).ToList();
        if (phones.Count > 0)
        {
            var targets = await (
                    from consent in db.WhatsAppConsents.IgnoreQueryFilters()
                    where consent.OptedIn
                    join customer in db.Customers.IgnoreQueryFilters() on consent.CustomerId equals customer.Id
                    where phones.Contains(customer.Phone)
                    select new { consent, customer.Phone })
                .Take(500)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var t in targets)
            {
                var at = parsed.StopAt.TryGetValue(t.Phone[1..], out var sent) && sent < now ? sent : now;
                if (t.consent.ChangedAtUtc > at)
                {
                    continue; // replayed/late STOP older than the latest opt-in: the newer choice stands
                }

                t.consent.Set(false, "reply", null, at);
                stopped.Add((t.consent.BusinessId, t.consent.CustomerId, t.Phone));
                optedOut++;
            }
        }

        if (updated > 0 || optedOut > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var (businessId, customerId, phone) in stopped)
        {
            await audit.WriteAsync(new AuditEntry(AuditActions.WhatsAppConsentChanged,
                $"Customer replied STOP on WhatsApp ({AuditMask.Phone(phone)}): automatic messages stopped",
                businessId, null, null, "customer", customerId),
                throttleKey: $"wa-stop:{customerId}", window: TimeSpan.FromHours(1), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return (updated, optedOut);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
