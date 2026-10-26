using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.WhatsApp;

/// <summary>
/// Tap-to-send (phone and web): the customer's number in WhatsApp form and a ready, editable message per status.
/// Works offline (no server call needed to build the link).
/// </summary>
public static class WhatsAppLinks
{
    public const int MaxText = 1000;

    /// <summary>
    /// International digits for wa.me (e.g. "233245550123"), or null when the number can't be a WhatsApp number.
    /// Ghana numbers in any common form ("024 555 0123", "+233 (0)24...") become 233XXXXXXXXX.
    /// </summary>
    public static string? ToWhatsAppDigits(string? phone)
    {
        if (PhoneNumbers.Check(phone) is not null)
        {
            return null;
        }

        var normalized = PhoneNumbers.Normalize(phone);
        if (!normalized.StartsWith('+'))
        {
            return null;
        }

        var digits = normalized[1..];
        if (IsGhanaLandline(digits))
        {
            return null; // 03X fixed lines can't have WhatsApp
        }

        return digits.Length is >= 8 and <= 15 && digits.All(char.IsAsciiDigit) && digits[0] != '0' ? digits : null;
    }

    /// <summary>Ghana-friendly message for the order's status; the person can edit it in WhatsApp before sending.</summary>
    public static string Message(string status, string? customerName, string? businessName, string? orderRef)
    {
        var name = FirstName(customerName);
        var hello = name is null ? "Hello" : $"Hello {name}";
        var shop = string.IsNullOrWhiteSpace(businessName) ? "your tailor" : businessName.Trim();
        var order = string.IsNullOrWhiteSpace(orderRef) ? "your order" : $"your order {orderRef.Trim()}";
        var body = status switch
        {
            OrderStatus.New => $"{hello}, this is {shop}. We have received {order}. We will let you know when it is ready. Thank you!",
            OrderStatus.Cutting => $"{hello}, this is {shop}. Good news: we have started cutting {order}.",
            OrderStatus.Sewing => $"{hello}, this is {shop}. {Cap(order)} is now being sewn.",
            OrderStatus.Fitting => $"{hello}, this is {shop}. {Cap(order)} is ready for fitting. Please come by when you are free.",
            OrderStatus.Ready => $"{hello}, this is {shop}. {Cap(order)} is ready for pickup. You are welcome any time during opening hours. Thank you!",
            OrderStatus.Collected => $"{hello}, this is {shop}. Thank you for collecting {order}. We hope you enjoy it!",
            OrderStatus.Cancelled => $"{hello}, this is {shop}. {Cap(order)} has been cancelled. Please call us if you have any questions.",
            _ => $"{hello}, this is {shop} about {order}."
        };
        return TextCut.Max(body, MaxText);
    }

    /// <summary>https://wa.me link (opens the app on phones, WhatsApp Web on computers); null for an unusable number.</summary>
    public static string? Url(string? phone, string text)
    {
        var digits = ToWhatsAppDigits(phone);
        return digits is null ? null : $"https://wa.me/{digits}?text={Uri.EscapeDataString(text ?? string.Empty)}";
    }

    /// <summary>whatsapp:// deep link (phone app directly); null for an unusable number.</summary>
    public static string? AppUri(string? phone, string text)
    {
        var digits = ToWhatsAppDigits(phone);
        return digits is null ? null : $"whatsapp://send?phone={digits}&text={Uri.EscapeDataString(text ?? string.Empty)}";
    }

    public const string NoNumberMessage = "This customer has no WhatsApp-ready phone number. Edit the customer's phone (e.g. 024 555 0123) and try again.";

    private static string? FirstName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var first = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return TextCut.Max(first, 40);
    }

    /// <summary>Ghana fixed lines are 03X (e.g. 030 2xx xxxx); mobiles are 02X/05X.</summary>
    public static bool IsGhanaLandline(string digits) => digits.Length == 12 && digits.StartsWith("2333", StringComparison.Ordinal);

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
