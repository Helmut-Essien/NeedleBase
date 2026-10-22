namespace NeedleBase.Application.Audit;

/// <summary>Masking for audit values: the log shows that something changed without copying personal data.</summary>
public static class AuditMask
{
    public const string Changed = "(changed)";
    public const string Hidden = "(hidden)";

    /// <summary>"+233245550123" → "+233•••••0123" (keeps country code and last 4 digits only).</summary>
    public static string? Phone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return phone;
        }

        var p = phone.Trim();
        if (p.Length <= 6)
        {
            return new string('•', p.Length);
        }

        var head = p.StartsWith('+') ? p[..Math.Min(4, p.Length - 4)] : string.Empty;
        return head + new string('•', 5) + p[^4..];
    }

    /// <summary>"ama.owusu@gmail.com" → "a•••@gmail.com".</summary>
    public static string Email(string? email)
    {
        var e = (email ?? string.Empty).Trim();
        var at = e.IndexOf('@');
        if (at <= 0)
        {
            return "•••";
        }

        return e[0] + "•••" + e[at..];
    }

    /// <summary>Short free text for a summary: trimmed, single line, at most <paramref name="max"/> chars.</summary>
    public static string Short(string? text, int max = 80)
    {
        var t = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (t.Length <= max)
        {
            return t;
        }

        var keep = max - 1;
        if (char.IsHighSurrogate(t[keep - 1]))
        {
            keep--; // never leave half an emoji (unencodable → the save would fail)
        }

        return t[..keep] + "…";
    }
}
