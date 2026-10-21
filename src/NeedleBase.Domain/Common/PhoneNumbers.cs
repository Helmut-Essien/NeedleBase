using System.Text;

namespace NeedleBase.Domain.Common;

/// <summary>
/// One phone rule for API, web and mobile. Ghana numbers are stored as +233XXXXXXXXX ("024 555 0123",
/// "233245550123", "+233 (0)24-555-0123" all become "+233245550123"); other "+"/"00" international numbers
/// keep their digits with a leading "+"; anything unrecognised is kept as typed (trimmed) so no data is lost.
/// <see cref="Normalize"/> never throws; <see cref="Check"/> is the strict format rule for forms/REST.
/// Changing <see cref="Normalize"/> needs a data step (server backfill + local schema step).
/// </summary>
public static class PhoneNumbers
{
    public const string GhanaPrefix = "+233";
    public const int MinDigits = 9;
    public const int MaxDigits = 15;
    public const string FormatMessage = "Enter a valid phone number, e.g. 024 555 0123 or +233 24 555 0123.";

    public static string Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var trimmed = phone.Trim();
        if (!OnlyPhoneChars(trimmed))
        {
            return trimmed; // e.g. letters/extension text: keep what the user typed
        }

        var digits = Digits(trimmed);
        var international = trimmed.StartsWith('+');
        if (!international && digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
            international = true;
        }

        if (digits.StartsWith("233", StringComparison.Ordinal) && (international || digits.Length >= 12))
        {
            var national = digits[3..];
            if (national.Length == 10 && national[0] == '0')
            {
                national = national[1..]; // "+233 (0)24..."
            }

            if (national.Length == 9 && national[0] != '0')
            {
                return GhanaPrefix + national;
            }
        }

        if (!international)
        {
            if (digits.Length == 10 && digits[0] == '0' && digits[1] != '0')
            {
                return GhanaPrefix + digits[1..];
            }

            return trimmed;
        }

        return digits.Length is >= MinDigits and <= MaxDigits ? "+" + digits : trimmed;
    }

    /// <summary>Null when <paramref name="phone"/> is an acceptable phone number; otherwise a user-facing message.</summary>
    public static string? Check(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return "Phone is required.";
        }

        var trimmed = phone.Trim();
        if (!OnlyPhoneChars(trimmed) || trimmed.LastIndexOf('+') > 0)
        {
            return FormatMessage;
        }

        var normalized = Normalize(trimmed);
        if (normalized.StartsWith(GhanaPrefix, StringComparison.Ordinal))
        {
            return normalized.Length == GhanaPrefix.Length + 9 ? null : FormatMessage;
        }

        var count = Digits(trimmed).Length;
        return count is >= MinDigits and <= MaxDigits ? null : FormatMessage;
    }

    /// <summary>Same number regardless of formatting ("024 555 0123" == "+233245550123").</summary>
    public static bool Same(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    private static bool OnlyPhoneChars(string s)
    {
        foreach (var ch in s)
        {
            if (!(char.IsAsciiDigit(ch) || ch is '+' or ' ' or '-' or '(' or ')' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static string Digits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsAsciiDigit(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }
}
