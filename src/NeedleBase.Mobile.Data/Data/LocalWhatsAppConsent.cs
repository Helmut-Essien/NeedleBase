namespace NeedleBase.Mobile.Data.Data;

/// <summary>
/// Phone copy of a customer's WhatsApp consent: last known server state (for offline display) and at most one
/// pending choice made on this phone. Not part of the sync queue: consent is pushed by <see cref="Services.WhatsAppConsentSync"/>.
/// </summary>
public sealed class LocalWhatsAppConsent
{
    public string CustomerId { get; set; } = string.Empty;
    public string BusinessId { get; set; } = string.Empty;
    public bool OptedIn { get; set; }

    /// <summary>"staff" or "reply" (customer replied STOP); null = never recorded.</summary>
    public string? Source { get; set; }

    public DateTimeOffset? ChangedAtUtc { get; set; }
    public bool? PendingOptedIn { get; set; }
    public DateTimeOffset? PendingAtUtc { get; set; }
}
