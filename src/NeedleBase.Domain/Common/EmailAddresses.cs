namespace NeedleBase.Domain.Common;

/// <summary>
/// Optional customer (and similar) email helpers. Empty → null. Present values must look like an address
/// (same bar as <see cref="Entities.Invitation"/>: non-empty local/domain around one <c>@</c>).
/// </summary>
public static class EmailAddresses
{
    /// <summary>Trim; whitespace-only becomes null. Does not lower-case (display as typed).</summary>
    public static string? NormalizeOptional(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return email.Trim();
    }

    /// <summary>
    /// Null when empty (optional) or valid. Otherwise a plain-language error for forms/API.
    /// </summary>
    public static string? CheckOptional(string? email)
    {
        var e = NormalizeOptional(email);
        if (e is null)
        {
            return null;
        }

        if (e.Length > FieldLimits.Email)
        {
            return $"Email must be at most {FieldLimits.Email} characters.";
        }

        if (e.Contains(' ', StringComparison.Ordinal) || e.Contains('\t', StringComparison.Ordinal))
        {
            return "Enter an email without spaces.";
        }

        var at = e.IndexOf('@');
        if (at <= 0 || at != e.LastIndexOf('@') || at >= e.Length - 1)
        {
            return "Enter a valid email address.";
        }

        return null;
    }
}
