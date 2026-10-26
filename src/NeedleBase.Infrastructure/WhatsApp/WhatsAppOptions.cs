namespace NeedleBase.Infrastructure.WhatsApp;

/// <summary>
/// "WhatsApp" section (Meta WhatsApp Business Cloud API). Secrets (AccessToken, AppSecret, VerifyToken) come only from
/// user-secrets or environment variables (WhatsApp__AccessToken, ...), never appsettings or source control.
/// Automatic messages are off unless <see cref="IsConfigured"/>; tap-to-send works without any of this.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string Section = "WhatsApp";

    /// <summary>Permanent System User token with whatsapp_business_messaging permission.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Phone number ID (not the phone number) from WhatsApp Manager → API Setup.</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>WhatsApp Business Account ID (reference/diagnostics; sending only needs the phone number ID).</summary>
    public string? BusinessAccountId { get; set; }

    /// <summary>Meta app secret: verifies the X-Hub-Signature-256 header on delivery webhooks.</summary>
    public string? AppSecret { get; set; }

    /// <summary>Any long random string; the same value is typed into Meta's webhook "Verify token" box.</summary>
    public string? VerifyToken { get; set; }

    public string ApiVersion { get; set; } = "v21.0";
    public string BaseUrl { get; set; } = "https://graph.facebook.com";

    /// <summary>Template language code exactly as approved in Meta (e.g. "en", "en_GB", "en_US").</summary>
    public string TemplateLanguage { get; set; } = "en";

    /// <summary>
    /// Approved template name per order status, e.g. Templates:Ready = "order_ready_for_pickup". A status without a
    /// template can't be chosen for automatic messages. Body parameters: {{1}} customer first name, {{2}} shop name,
    /// {{3}} order reference.
    /// </summary>
    public Dictionary<string, string> Templates { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Wait after a status change before sending (quick status flips send nothing or only the final one).</summary>
    public int DelaySeconds { get; set; } = 120;

    /// <summary>Most automatic messages one business may send per hour (protects cost and Meta quality rating).</summary>
    public int MaxPerBusinessPerHour { get; set; } = 100;

    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(PhoneNumberId) &&
        Templates.Any(t => !string.IsNullOrWhiteSpace(t.Value));

    public bool WebhookConfigured => !string.IsNullOrWhiteSpace(AppSecret) && !string.IsNullOrWhiteSpace(VerifyToken);

    public string? TemplateFor(string status) =>
        Templates.TryGetValue(status, out var name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;
}
