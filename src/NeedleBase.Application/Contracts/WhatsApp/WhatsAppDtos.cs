namespace NeedleBase.Application.Contracts.WhatsApp;

/// <summary>Automatic WhatsApp settings for the caller's business plus what the server can do.</summary>
/// <param name="Configured">Server has Meta credentials and at least one template; false = automatic messages unavailable (tap-to-send still works).</param>
/// <param name="AvailableStatuses">Statuses with an approved template configured on the server.</param>
public sealed record WhatsAppSettingsDto(
    bool Configured,
    bool Enabled,
    IReadOnlyList<string> Statuses,
    IReadOnlyList<string> AvailableStatuses,
    int DelayMinutes,
    DateTimeOffset? UpdatedAtUtc);

public sealed class UpdateWhatsAppSettingsRequest
{
    public bool Enabled { get; set; }
    public List<string>? Statuses { get; set; }
}

public sealed record WhatsAppConsentDto(string CustomerId, bool OptedIn, string? Source, DateTimeOffset? ChangedAtUtc);

public sealed class SetWhatsAppConsentRequest
{
    public bool OptedIn { get; set; }

    /// <summary>
    /// When the choice was made (phone, possibly offline). A newer change already on the server (another
    /// device, or the customer replying STOP) wins and is returned unchanged. Null = now (web). Future values are clamped.
    /// </summary>
    public DateTimeOffset? ChangedAtUtc { get; set; }

    /// <summary>
    /// The sending device's clock when the request was sent. Lets the server correct <see cref="ChangedAtUtc"/> for a
    /// phone whose clock is wrong (changed = server now - (device now - device changed)). Null = trust ChangedAtUtc.
    /// </summary>
    public DateTimeOffset? DeviceNowUtc { get; set; }
}

/// <summary>One automatic message in the log (phone masked).</summary>
public sealed record WhatsAppMessageDto(
    string Id,
    string OrderId,
    string? OrderRef,
    string? CustomerName,
    string? MaskedPhone,
    string OrderStatus,
    string State,
    int Attempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? Error);

public sealed record WhatsAppMessagePageDto(IReadOnlyList<WhatsAppMessageDto> Items, int Page, int PageSize, bool HasMore);

/// <summary>Tap-to-send: the phone/web opened WhatsApp with a prefilled message (not proof it was sent).</summary>
public sealed class WhatsAppOpenedRequest
{
    public string? Status { get; set; }
}

public static class WhatsAppErrorCodes
{
    public const string NotConfigured = "WHATSAPP_NOT_CONFIGURED";
    public const string NoTemplate = "WHATSAPP_NO_TEMPLATE";
    public const string NotFound = "NOT_FOUND";
}
