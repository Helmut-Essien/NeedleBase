namespace NeedleBase.Web.Marketing;

/// <summary>
/// Public marketing contacts and placeholder prices for the landing page.
/// PLACEHOLDER PRICING — replace with real prices.
/// Edit this file only when changing WhatsApp, email, or plan amounts.
/// </summary>
public static class LandingSalesInfo
{
    public const string WhatsAppE164 = "233500785153"; // +233 50 078 5153
    public const string WhatsAppDisplay = "+233 50 078 5153";
    public const string Email = "helmutessien@gmail.com";

    public const string DefaultWhatsAppMessage = "Hello, I'd like to get NeedleBase for my shop";
    public const string EmailSubject = "NeedleBase for my shop";

    public static string WhatsAppUrl(string? message = null)
    {
        var text = Uri.EscapeDataString(message ?? DefaultWhatsAppMessage);
        return $"https://wa.me/{WhatsAppE164}?text={text}";
    }

    public static string WhatsAppUrlForPlan(string planName) =>
        WhatsAppUrl($"Hello, I'd like the {planName} plan for NeedleBase for my shop");

    public static string MailtoUrl(string? subject = null)
    {
        var s = Uri.EscapeDataString(subject ?? EmailSubject);
        return $"mailto:{Email}?subject={s}";
    }

    // PLACEHOLDER PRICING — replace with real prices (GHS).
    // Yearly = 10× monthly (two months free).
    public static IReadOnlyList<LandingPlan> Plans { get; } =
    [
        new LandingPlan(
            Id: "starter",
            Name: "Starter",
            Tagline: "Owner plus a small team",
            MonthlyGhs: 149,
            YearlyGhs: 1_490,
            Highlights:
            [
                "Owner plus a helper",
                "Up to 2 staff",
                "Phone and computer",
                "Orders, measurements, payments"
            ],
            Featured: false),
        new LandingPlan(
            Id: "workshop",
            Name: "Workshop",
            Tagline: "Busy tailor and fashion workshops",
            MonthlyGhs: 349,
            YearlyGhs: 3_490,
            Highlights:
            [
                "A busy tailor or fashion workshop",
                "Up to 8 staff",
                "Invite managers and tailors",
                "Phone and computer",
                "Orders, measurements, payments"
            ],
            Featured: true),
        new LandingPlan(
            Id: "production",
            Name: "Production",
            Tagline: "Larger workrooms",
            MonthlyGhs: 649,
            YearlyGhs: 6_490,
            Highlights:
            [
                "A larger workroom",
                "Up to 20 staff",
                "Invite managers and tailors",
                "Phone and computer",
                "Orders, measurements, payments"
            ],
            Featured: false)
    ];
}

public sealed record LandingPlan(
    string Id,
    string Name,
    string Tagline,
    int MonthlyGhs,
    int YearlyGhs,
    IReadOnlyList<string> Highlights,
    bool Featured)
{
    public int YearlySavingGhs => (MonthlyGhs * 12) - YearlyGhs;
}
