using System.Security.Cryptography;
using System.Text;

namespace NeedleBase.Application.Staff;

/// <summary>
/// One-time invite codes: 12 characters from an alphabet without look-alikes (no 0/O, 1/I/L), shown as XXXX-XXXX-XXXX
/// so they read out well over the phone. About 59 bits; accept/preview are rate limited and throttled.
/// </summary>
public static class InviteCodes
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 12;

    public static string NewCode()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        var s = new string(chars);
        return $"{s[..4]}-{s[4..8]}-{s[8..]}";
    }

    /// <summary>Uppercase, separators/spaces dropped, look-alikes mapped (O→0 is not in the alphabet, so O stays invalid).</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var sb = new StringBuilder(Length);
        foreach (var ch in code)
        {
            if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '.')
            {
                continue;
            }

            var u = char.ToUpperInvariant(ch);
            if (Alphabet.IndexOf(u) < 0 || sb.Length == Length)
            {
                return null;
            }

            sb.Append(u);
        }

        return sb.Length == Length ? sb.ToString() : null;
    }

    public static string? Hash(string? code) =>
        Normalize(code) is { } n ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(n))) : null;
}
