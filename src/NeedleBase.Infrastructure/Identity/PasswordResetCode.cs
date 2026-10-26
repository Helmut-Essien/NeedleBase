using System.Security.Cryptography;
using System.Text;

namespace NeedleBase.Infrastructure.Identity;

/// <summary>
/// A one-time password reset code sent by email. Only a hash is stored (salted with the user id); it expires after
/// <see cref="Lifetime"/>, works once, and dies after <see cref="MaxAttempts"/> wrong tries.
/// </summary>
public class PasswordResetCode
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    public const int MaxAttempts = 5;
    public const int Digits = 8;

    /// <summary>New codes per account per hour (more requests are silently ignored).</summary>
    public const int MaxPerHour = 3;

    public string Id { get; set; } = string.Empty;
    public string ApplicationUserId { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public int FailedAttempts { get; set; }

    public ApplicationUser? ApplicationUser { get; set; }

    public bool IsUsable(DateTimeOffset now) => UsedAtUtc is null && ExpiresAtUtc > now && FailedAttempts < MaxAttempts;

    /// <summary>8 digits, shown as "1234 5678" (easy to read off a phone and type on a keypad).</summary>
    public static string NewCode()
    {
        var n = RandomNumberGenerator.GetInt32(0, 100_000_000);
        var s = n.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        return $"{s[..4]} {s[4..]}";
    }

    /// <summary>Digits only, or null when it isn't 8 digits.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var digits = new string(code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return digits.Length == Digits && digits.All(c => c is >= '0' and <= '9') ? digits : null;
    }

    public static string Hash(string userId, string normalizedCode) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId + ":" + normalizedCode)));
}
