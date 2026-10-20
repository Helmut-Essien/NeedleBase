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

    /// <summary>
    /// Folded, separator-collapsed name with a leading space: "Adwóa  Mensah-Owusu" → " adwoa mensah owusu".
    /// Folding: lower case, accents removed (FormD), Akan letters ɛ/ɔ/ŋ read as e/o/n; hyphens and apostrophes
    /// split words like spaces. Changing these rules needs a local schema step that resets SearchName.
    /// </summary>
    public static string NameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return " ";
        }

        var (folded, _) = FoldWithMap(name);
        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? " " : " " + string.Join(' ', words);
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
    public static int WordPrefixIndex(string name, string? term) => WordPrefixMatch(name, term).Start;

    /// <summary>
    /// Where a folded <paramref name="term"/> (as produced by <see cref="Parse"/>) matches a word start in the
    /// original <paramref name="name"/>: start and length in the original string (accents, combining marks and
    /// surrogate pairs included), or (-1, 0).
    /// </summary>
    public static (int Start, int Length) WordPrefixMatch(string name, string? term)
    {
        if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(name))
        {
            return (-1, 0);
        }

        var foldedTerm = NameKey(term).TrimStart();
        var (folded, map) = FoldWithMap(name);
        for (var i = 0; i + foldedTerm.Length <= folded.Length; i++)
        {
            var atWordStart = folded[i] != ' ' && (i == 0 || folded[i - 1] == ' ');
            if (atWordStart && string.CompareOrdinal(folded, i, foldedTerm, 0, foldedTerm.Length) == 0)
            {
                var start = map[i];
                var end = map[i + foldedTerm.Length - 1] + 1;
                if (end < name.Length && char.IsLowSurrogate(name[end]))
                {
                    end++;
                }

                while (end < name.Length &&
                       System.Globalization.CharUnicodeInfo.GetUnicodeCategory(name[end]) == System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    end++; // keep a trailing combining accent inside the highlight
                }

                return (start, end - start);
            }
        }

        return (-1, 0);
    }

    /// <summary>Word separators besides white space: hyphens and apostrophes ("Mensah-Owusu", "N'Guessan").</summary>
    public static bool IsSeparator(char c) =>
        char.IsWhiteSpace(c) || c is '-' or '\'' or '\u2010' or '\u2011' or '\u2018' or '\u2019' or '`';

    /// <summary>Folded text (single spaces for separators, no leading space) plus each folded char's index in the source.</summary>
    private static (string Folded, List<int> Map) FoldWithMap(string source)
    {
        var sb = new StringBuilder(source.Length);
        var map = new List<int>(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];
            if (IsSeparator(ch))
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                    map.Add(i);
                }

                continue;
            }

            if (char.IsSurrogate(ch))
            {
                sb.Append(ch);
                map.Add(i);
                continue;
            }

            foreach (var f in FoldChar(ch))
            {
                sb.Append(f);
                map.Add(i);
            }
        }

        if (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
            map.RemoveAt(map.Count - 1);
        }

        return (sb.ToString(), map);
    }

    private static string FoldChar(char ch)
    {
        switch (ch)
        {
            case 'ɛ' or 'Ɛ':
                return "e";
            case 'ɔ' or 'Ɔ':
                return "o";
            case 'ŋ' or 'Ŋ':
                return "n";
        }

        var decomposed = ch.ToString().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var d in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(d) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(d));
            }
        }

        return sb.ToString();
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
