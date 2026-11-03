using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Infrastructure.Licensing;

/// <summary>
/// Verifies Platform's license token: compact JWS, header alg ES256 (raw r||s signature, .NET's IEEE P1363 default),
/// key by <c>kid</c> from Platform's JWKS. Checks audience (service code), device (shop id), issuer (when configured),
/// expiry (= grace_until) and that it is the license Platform just answered for. The verified claims, not the plain
/// JSON fields, become the cached license.
/// </summary>
public static class LicenseTokenVerifier
{
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    public sealed record Expectation(string Audience, string DeviceId, string? Issuer, string? LicenseId);

    /// <returns>The check built from the claims, or null with <paramref name="error"/> set.</returns>
    public static LicenseCheck? Verify(
        string? token,
        IReadOnlyDictionary<string, ECParameters> keys,
        Expectation expect,
        DateTimeOffset now,
        out string? error,
        out string? kid)
    {
        kid = null;
        error = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192)
        {
            error = "token_missing";
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            error = "token_malformed";
            return null;
        }

        try
        {
            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "ES256")
            {
                error = "token_alg";
                return null;
            }

            kid = header.RootElement.TryGetProperty("kid", out var k) ? k.GetString() : null;
            if (kid is null || !keys.TryGetValue(kid, out var parameters))
            {
                error = "token_unknown_kid";
                return null;
            }

            var signature = Base64UrlDecode(parts[2]);
            if (signature.Length != 64)
            {
                error = "token_signature";
                return null;
            }

            using (var ecdsa = ECDsa.Create(parameters))
            {
                var signed = Encoding.ASCII.GetBytes(string.Concat(parts[0], ".", parts[1]));
                if (!ecdsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                {
                    error = "token_signature";
                    return null;
                }
            }

            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var c = payload.RootElement;
            if (!AudienceMatches(c, expect.Audience))
            {
                error = "token_audience";
                return null;
            }

            if (Str(c, "device_id") is not { } device || !string.Equals(device, expect.DeviceId, StringComparison.Ordinal))
            {
                error = "token_device";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(expect.Issuer) && Str(c, "iss") != expect.Issuer)
            {
                error = "token_issuer";
                return null;
            }

            var licenseId = Str(c, "license_id") ?? Str(c, "sub");
            if (licenseId is null || (expect.LicenseId is not null && licenseId != expect.LicenseId))
            {
                error = "token_license";
                return null;
            }

            if (Time(c, "exp") is not { } exp || exp + Skew < now)
            {
                error = "token_expired";
                return null;
            }

            return new LicenseCheck(
                IsValid: true,
                Reason: "ok",
                LicenseId: licenseId,
                Plan: Str(c, "plan"),
                IsTrial: Bool(c, "trial"),
                IsComplimentary: Bool(c, "complimentary"),
                ExpiresAtUtc: Time(c, "license_expires_at"),
                RefreshAfterUtc: Time(c, "refresh_after"),
                GraceUntilUtc: Time(c, "grace_until") ?? exp,
                Token: token);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or InvalidOperationException or ArgumentException)
        {
            error = "token_malformed";
            return null;
        }
    }

    private static bool AudienceMatches(JsonElement c, string audience)
    {
        if (!c.TryGetProperty("aud", out var aud))
        {
            return false;
        }

        return aud.ValueKind switch
        {
            JsonValueKind.String => string.Equals(aud.GetString(), audience, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => aud.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.String &&
                                                                 string.Equals(a.GetString(), audience, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static string? Str(JsonElement c, string name) =>
        c.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement c, string name) =>
        c.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? Time(JsonElement c, string name) =>
        c.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var s) &&
        s > 0 && s < 253402300799
            ? DateTimeOffset.FromUnixTimeSeconds(s)
            : null;

    public static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", 0 => s, _ => throw new FormatException("bad base64url") };
        return Convert.FromBase64String(s);
    }
}
