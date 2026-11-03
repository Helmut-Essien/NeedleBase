namespace NeedleBase.Application.Licensing;

/// <summary>Stable codes + friendly copy for license problems (registration, attach, read-only writes).</summary>
public static class LicenseErrors
{
    public const string KeyRequired = "LICENSE_KEY_REQUIRED";
    public const string KeyInvalid = "LICENSE_KEY_INVALID";
    public const string KeyInUse = "LICENSE_KEY_IN_USE";
    public const string EmailMismatch = "LICENSE_EMAIL_MISMATCH";
    public const string Expired = "LICENSE_EXPIRED";
    public const string Suspended = "LICENSE_SUSPENDED";
    public const string Revoked = "LICENSE_REVOKED";
    public const string NotActive = "LICENSE_NOT_ACTIVE";
    public const string CheckUnavailable = "LICENSE_CHECK_UNAVAILABLE";
    public const string NotConfigured = "LICENSE_NOT_CONFIGURED";
    public const string ReadOnly = "SUBSCRIPTION_READ_ONLY";

    public const string ReadOnlyMessage =
        "Your NeedleBase license needs attention, so new changes are paused. You can still view your records. Ask the shop owner to renew it.";

    /// <summary>Copy for a code (null when the code isn't a license code).</summary>
    public static string? MessageFor(string? code) => code switch
    {
        KeyRequired => "Enter the license key from the email we sent you.",
        KeyInvalid => "That license key wasn't recognised. Check it against the email we sent you and try again.",
        KeyInUse => "This license key is already used by another shop. Each key works for one shop — contact us if this is your key.",
        EmailMismatch => "This email doesn't match the license. Use the email address the license key was sent to.",
        Expired => "This license has expired. Renew it, then try again.",
        Suspended => "This license is suspended. Contact us to sort it out.",
        Revoked => "This license has been cancelled. Contact us for a new key.",
        NotActive => "This license isn't active yet. Contact us to activate it.",
        CheckUnavailable => "We couldn't check your license key just now, so nothing was created. Please try again in a few minutes.",
        NotConfigured => "Sign-up isn't available right now. Please try again later or contact us.",
        ReadOnly => ReadOnlyMessage,
        _ => null
    };

    /// <summary>
    /// Registration / attach decision. Only a valid license (active, trial, complimentary) whose customer email
    /// matches passes; a missing email-match answer counts as a failure (refuse rather than guess).
    /// Returns null when allowed, otherwise the error code.
    /// </summary>
    public static string? Decide(LicensePlatformOutcome outcome)
    {
        if (outcome.Unavailable || outcome.Check is null)
        {
            return CheckUnavailable;
        }

        if (!outcome.Check.IsValid)
        {
            return ForReason(outcome.Check.Reason);
        }

        return outcome.EmailMatches == true ? null : EmailMismatch;
    }

    /// <summary>Platform reason → code (only reasons about the key itself; the client turns the rest into "unavailable").</summary>
    public static string ForReason(string? reason) => reason switch
    {
        "invalid_key" => KeyInvalid,
        "device_limit" => KeyInUse,
        "expired" => Expired,
        "suspended" => Suspended,
        "revoked" => Revoked,
        "not_active" => NotActive,
        _ => CheckUnavailable
    };

    /// <summary>Short phrase for banners: "Your NeedleBase license {phrase}."</summary>
    public static string Phrase(string? reason) => reason switch
    {
        "expired" => "has expired",
        "suspended" => "is suspended",
        "revoked" => "has been cancelled",
        "not_active" => "isn't active",
        "device_limit" => "is being used by another shop",
        "invalid_key" => "key is no longer recognised",
        _ => "needs attention"
    };
}
