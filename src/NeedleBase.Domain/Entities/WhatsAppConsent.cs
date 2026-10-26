namespace NeedleBase.Domain.Entities;

/// <summary>
/// A customer's consent to automatic WhatsApp messages (WhatsApp policy: opt-in first). Id = CustomerId.
/// No row, or <see cref="OptedIn"/> false, means no automatic messages. Opt-out (in the app or by replying STOP)
/// is kept, with who/when, and wins until someone records a new opt-in.
/// </summary>
public class WhatsAppConsent : Entity, IBusinessScoped
{
    public const int SourceMax = 16;

    public string BusinessId { get; private set; } = string.Empty;
    public string CustomerId { get; private set; } = string.Empty;
    public bool OptedIn { get; private set; }

    /// <summary>"staff" (recorded in the app) or "reply" (customer replied STOP).</summary>
    public string Source { get; private set; } = "staff";

    public string? ChangedByUserId { get; private set; }
    public DateTimeOffset ChangedAtUtc { get; private set; }

    private WhatsAppConsent()
    {
    }

    public static WhatsAppConsent Create(string businessId, string customerId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        return new WhatsAppConsent { Id = customerId, BusinessId = businessId, CustomerId = customerId, ChangedAtUtc = now };
    }

    public void Set(bool optedIn, string source, string? userId, DateTimeOffset now)
    {
        OptedIn = optedIn;
        Source = source.Length > SourceMax ? source[..SourceMax] : source;
        ChangedByUserId = userId;
        ChangedAtUtc = now;
    }
}
