using System.Text;

namespace NeedleBase.Application.Common;

/// <summary>Parsed search-as-you-type query. Either part may be null.</summary>
public sealed record CustomerSearchQuery(string? NameTerm, string? PhoneTerm)
{
    public bool IsEmpty => NameTerm is null && PhoneTerm is null;
}

/// <summary>
/// One matching rule for customer search (mobile local store; Api can adopt it).
/// Name: case-insensitive, any word prefix ("men" finds "Efua Mensah").
/// Phone: national digits prefix, so "024", "24 123", "0244" and "+23324" all match +233 24 ...
/// Stored keys are precomputed (<see cref="NameKey"/>, <see cref="PhoneDigits"/>) so SQL stays a LIKE on an indexed column.
/// </summary>
public static class CustomerSearchKeys
{
    private const string GhanaCountryCode = "233";

    /// <summary>Lower-cased, whitespace-collapsed name with a leading space: " efua mensah".</summary>
    public static string NameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return " ";
        }

        var words = name.Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return " " + string.Join(' ', words);
    }

    /// <summary>National significant digits: "+233 24 555 0123" and "024 555 0123" both give "245550123".</summary>
    public static string PhoneDigits(string? phone)
    {
        var digits = DigitsOnly(phone);
        return StripPrefix(digits);
    }

    public static CustomerSearchQuery Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new CustomerSearchQuery(null, null);
        }

        var trimmed = query.Trim();
        var hasLetters = trimmed.Any(char.IsLetter);

        string? phone = null;
        if (!hasLetters)
        {
            var digits = DigitsOnly(trimmed);
            if (digits.Length >= 2)
            {
                var national = StripPrefix(digits);
                phone = national.Length > 0 ? national : null;
            }
        }

        string? name = null;
        if (hasLetters)
        {
            var cleaned = new StringBuilder(trimmed.Length);
            foreach (var ch in trimmed)
            {
                // LIKE wildcards/escape are dropped so user input can't widen the match.
                if (ch is '%' or '_' or '\\')
                {
                    continue;
                }

                cleaned.Append(ch);
            }

            var key = NameKey(cleaned.ToString()).TrimStart();
            name = key.Length > 0 ? key : null;
        }

        return new CustomerSearchQuery(name, phone);
    }

    /// <summary>Display format for Ghana numbers: "+233 24 555 0123"; other input is returned trimmed.</summary>
    public static string FormatPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var national = PhoneDigits(phone);
        var raw = DigitsOnly(phone);
        var looksGhanaian = national.Length == 9 &&
            (raw.StartsWith(GhanaCountryCode, StringComparison.Ordinal) || raw.StartsWith('0'));
        return looksGhanaian
            ? $"+233 {national[..2]} {national[2..5]} {national[5..]}"
            : phone.Trim();
    }

    /// <summary>Index of the first word in <paramref name="name"/> that starts with <paramref name="term"/>, or -1.</summary>
    public static int WordPrefixIndex(string name, string? term)
    {
        if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(name))
        {
            return -1;
        }

        for (var i = 0; i <= name.Length - term.Length; i++)
        {
            var atWordStart = i == 0 || char.IsWhiteSpace(name[i - 1]);
            if (atWordStart && string.Compare(name, i, term, 0, term.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static string DigitsOnly(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is >= '0' and <= '9')
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string StripPrefix(string digits)
    {
        if (digits.StartsWith(GhanaCountryCode, StringComparison.Ordinal))
        {
            return digits[GhanaCountryCode.Length..];
        }

        return digits.StartsWith('0') ? digits[1..] : digits;
    }
}
