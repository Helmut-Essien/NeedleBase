using System.Security.Cryptography;
using System.Text;

namespace NeedleBase.Infrastructure.Identity;

/// <summary>One-way hash for refresh tokens at rest (raw token returned to client only).</summary>
internal static class RefreshTokenHasher
{
    public static string Hash(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}
