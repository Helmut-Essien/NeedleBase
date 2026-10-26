namespace NeedleBase.Application.Common;

/// <summary>
/// The one password rule: the server's Identity options, the web forms and the phone all read it from here.
/// 6+ characters with an ASCII digit (0-9) and a lowercase ASCII letter (a-z), matching Identity's checks.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 6;
    public const int MaxLength = 100;

    /// <summary>Shown under every new-password field.</summary>
    public const string Hint = "At least 6 characters, with a number and a small letter";

    /// <summary>For [RegularExpression] on web forms (length checked separately).</summary>
    public const string Pattern = @"^(?=.*[0-9])(?=.*[a-z]).+$";

    public static bool IsStrongEnough(string? value) =>
        value is { Length: >= MinLength and <= MaxLength } &&
        value.Any(c => c is >= '0' and <= '9') &&
        value.Any(c => c is >= 'a' and <= 'z');
}
