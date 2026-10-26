namespace NeedleBase.Infrastructure.Email;

/// <summary>
/// "Email" section. Gmail: Host smtp.gmail.com, Port 587 (STARTTLS), Username = the Gmail address, Password = a Google
/// App Password (needs 2-Step Verification; never the normal Gmail password). Set the password with user-secrets or the
/// Email__Password environment variable only — never in appsettings. Gmail allows roughly 500 messages a day.
/// </summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "NeedleBase";

    /// <summary>Web app address used for links in emails (falls back to FrontendBaseUrl); links are left out when unset.</summary>
    public string? AppBaseUrl { get; set; }

    /// <summary>Per SMTP attempt (connect + send).</summary>
    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && Port is > 0 and < 65536 &&
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(EffectiveFrom);

    public string? EffectiveFrom => string.IsNullOrWhiteSpace(FromAddress) ? Username : FromAddress;
}
