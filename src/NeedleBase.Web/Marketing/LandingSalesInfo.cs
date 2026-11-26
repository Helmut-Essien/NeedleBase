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

    public static string WhatsAppUrlForPaidPlan() =>
        WhatsAppUrl("Hello, I'd like a paid plan for NeedleBase for my shop");

    public static string WhatsAppHelpUrl() =>
        WhatsAppUrl("Hello, I need help with NeedleBase");

    public static string WhatsAppFeedbackUrl() =>
        WhatsAppUrl("Hello, I'd like to send feedback about NeedleBase");

    public static string MailtoUrl(string? subject = null)
    {
        var s = Uri.EscapeDataString(subject ?? EmailSubject);
        return $"mailto:{Email}?subject={s}";
    }

    public static string MailtoFeedbackUrl() => MailtoUrl("Feedback on NeedleBase");

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
                new("Up to 10 customers and 20 orders"),
                new("Send messages on WhatsApp"),
                new("Save a copy of your records")
            ],
            Limitations:
            [
                "One phone only",
                "No staff and no computer",
                "Records stay on that phone unless you save a copy"
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
                new("You plus 2 staff"),
                new("Unlimited customers and orders"),
                new("Works on all your shop's phones and computers"),
                new("Your records are kept safe online"),
                new("Your shop's phone number on receipts"),
                new("A monthly summary email")
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
                new("You plus 8 staff"),
                new("Everything in Starter"),
                new("Staff job and due emails"),
                new("A weekly summary with balances owed"),
                new("PDF payment receipts"),
                new("Monthly reports of money in and owed")
            ],
            ComingSoonNote: "Coming soon: photos",
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
                new("You plus 20 staff"),
                new("Everything in Workshop"),
                new("A setup call")
            ],
            ComingSoonNote: "Coming soon: more photo storage",
            Featured: false)
    ];
}

public sealed record LandingHighlight(string Text, bool ComingSoon = false);

public sealed record LandingPlan(
    string Id,
    string Name,
    string Tagline,
    int MonthlyGhs,
    int QuarterlyGhs,
    int YearlyGhs,
    IReadOnlyList<LandingHighlight> Highlights,
    bool Featured,
    bool IsFree = false,
    IReadOnlyList<string>? Limitations = null,
    string? Footnote = null,
    string? ComingSoonNote = null)
{
    public IReadOnlyList<string> LimitationItems => Limitations ?? [];

    public int YearlySavingGhs => MonthlyGhs <= 0 ? 0 : (MonthlyGhs * 12) - YearlyGhs;

    public string MonthlyAlt =>
        IsFree ? "" : $"or GH₵{QuarterlyGhs.ToString("N0")} / 3 months · GH₵{YearlyGhs.ToString("N0")} / year";

    public string QuarterlyAlt =>
        IsFree ? "" : $"or GH₵{MonthlyGhs.ToString("N0")} / month · GH₵{YearlyGhs.ToString("N0")} / year";

    public string YearlyAlt =>
        YearlySavingGhs <= 0 ? "" : $"12 months for the price of 10 (save GH₵{YearlySavingGhs.ToString("N0")})";
}
