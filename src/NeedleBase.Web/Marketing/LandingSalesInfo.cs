namespace NeedleBase.Web.Marketing;

/// <summary>
/// Public marketing contacts and published plan prices for the landing page.
/// Edit this file only when changing WhatsApp, email, or plan amounts.
/// Prices are in Ghana cedis and exclude VAT. A year costs 10 months.
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

    public static IReadOnlyList<LandingPlan> Plans { get; } =
    [
        new LandingPlan(
            Id: "free",
            Name: "Free",
            Tagline: "Try it on your phone",
            MonthlyGhs: 0,
            QuarterlyGhs: 0,
            YearlyGhs: 0,
            Highlights:
            [
                "On one phone",
                "Up to 10 customers and 20 orders",
                "Send messages on WhatsApp",
                "Save a copy of your records"
            ],
            Limitations:
            [
                "Staff can't join",
                "Not on a computer or the website",
                "Deleting a customer or an order doesn't free a slot",
                "Records stay on that phone only — if it's lost or the app is removed, they're gone unless you saved a copy",
                "No summary emails"
            ],
            Footnote: "If you move to a paid plan, your records come with you.",
            Featured: false,
            IsFree: true),
        new LandingPlan(
            Id: "starter",
            Name: "Starter",
            Tagline: "You plus a small team",
            MonthlyGhs: 50,
            QuarterlyGhs: 140,
            YearlyGhs: 500,
            Highlights:
            [
                "You plus 2 staff",
                "Unlimited customers and orders",
                "Works on all your shop's phones and computers",
                "Your records are kept safe online",
                "A monthly summary email",
                "Your shop's phone number on receipts"
            ],
            Featured: false),
        new LandingPlan(
            Id: "workshop",
            Name: "Workshop",
            Tagline: "A busy workshop",
            MonthlyGhs: 150,
            QuarterlyGhs: 430,
            YearlyGhs: 1_500,
            Highlights:
            [
                "You plus 8 staff",
                "Everything in Starter",
                "Staff get an email when a job is given to them or is due",
                "Weekly and monthly summaries, including money owed",
                "Photos",
                "PDF receipts",
                "Profit and monthly reports"
            ],
            Featured: true),
        new LandingPlan(
            Id: "production",
            Name: "Production",
            Tagline: "A larger workroom",
            MonthlyGhs: 300,
            QuarterlyGhs: 850,
            YearlyGhs: 3_000,
            Highlights:
            [
                "You plus 20 staff",
                "Everything in Workshop",
                "More photo storage",
                "A setup call"
            ],
            Featured: false)
    ];
}

public sealed record LandingPlan(
    string Id,
    string Name,
    string Tagline,
    int MonthlyGhs,
    int QuarterlyGhs,
    int YearlyGhs,
    IReadOnlyList<string> Highlights,
    bool Featured,
    bool IsFree = false,
    IReadOnlyList<string>? Limitations = null,
    string? Footnote = null)
{
    public IReadOnlyList<string> LimitationItems => Limitations ?? [];

    public int YearlySavingGhs => MonthlyGhs <= 0 ? 0 : (MonthlyGhs * 12) - YearlyGhs;

    public string MonthlyAlt =>
        IsFree ? "" : $"or GH₵ {QuarterlyGhs.ToString("N0")} / quarter · GH₵ {YearlyGhs.ToString("N0")} / year";

    public string QuarterlyAlt =>
        IsFree ? "" : $"or GH₵ {MonthlyGhs.ToString("N0")} / month · GH₵ {YearlyGhs.ToString("N0")} / year";

    public string YearlyAlt =>
        YearlySavingGhs <= 0 ? "" : $"That's 10 months for the price of 12 (save GH₵ {YearlySavingGhs.ToString("N0")})";
}
