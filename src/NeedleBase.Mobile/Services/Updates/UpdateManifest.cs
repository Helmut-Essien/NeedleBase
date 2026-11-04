using System.Globalization;
using System.Text.Json;

namespace NeedleBase.Mobile.Services.Updates;

/// <summary>
/// One release as described by <c>update.json</c> on GitHub Pages (written by .github/workflows/release.yml).
/// <see cref="ApkUrl"/> is absolute (resolved against the manifest address).
/// </summary>
public sealed record UpdateManifest(
    string PackageName,
    string VersionName,
    int VersionCode,
    int MinSupportedVersionCode,
    Uri ApkUrl,
    string Sha256,
    long Size,
    IReadOnlyList<string> SigningCertSha256,
    string ReleaseNotes,
    DateTimeOffset? PublishedAt);

public sealed record ManifestParseResult(UpdateManifest? Manifest, string? Error)
{
    public bool Ok => Manifest is not null;
}

/// <summary>Strict reader for update.json: anything odd is rejected rather than half-trusted.</summary>
public static class UpdateManifestParser
{
    public const int SupportedSchema = 1;
    public const long MaxApkBytes = 400L * 1024 * 1024;
    public const int MaxNotesLength = 2000;
    public const int MaxVersionCode = 999_999_999;

    public static ManifestParseResult Parse(string? json, Uri manifestUrl, string expectedPackage)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail("empty manifest");
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail("manifest is not a JSON object");
            }

            if (!TryInt(root, "schema", out var schema) || schema != SupportedSchema)
            {
                return Fail($"unsupported manifest schema (this app reads {SupportedSchema})");
            }

            var package = Str(root, "packageName");
            if (!string.Equals(package, expectedPackage, StringComparison.Ordinal))
            {
                return Fail($"manifest is for '{package}', not this app");
            }

            var versionName = Str(root, "versionName");
            if (string.IsNullOrWhiteSpace(versionName) || versionName.Length > 32)
            {
                return Fail("versionName missing");
            }

            if (!TryInt(root, "versionCode", out var code) || code < 1 || code > MaxVersionCode)
            {
                return Fail("versionCode must be 1..999999999");
            }

            var min = 0;
            if (root.TryGetProperty("minSupportedVersionCode", out var minElement) && minElement.ValueKind != JsonValueKind.Null &&
                (!TryInt(root, "minSupportedVersionCode", out min) || min < 0 || min > code))
            {
                return Fail("minSupportedVersionCode must be 0..versionCode");
            }

            var apk = Str(root, "apkUrl");
            if (string.IsNullOrWhiteSpace(apk) || !Uri.TryCreate(manifestUrl, apk, out var apkUrl) || apkUrl.Scheme != Uri.UriSchemeHttps)
            {
                return Fail("apkUrl must be an https address");
            }

            var sha = Str(root, "sha256")?.Trim().ToLowerInvariant();
            if (sha is null || !IsHex(sha, 64))
            {
                return Fail("sha256 must be 64 hex characters");
            }

            if (!root.TryGetProperty("size", out var sizeElement) || !sizeElement.TryGetInt64(out var size) || size < 1 || size > MaxApkBytes)
            {
                return Fail("size must be 1 byte .. 400 MB");
            }

            var certs = new List<string>();
            if (root.TryGetProperty("signingCertSha256", out var certElement) && certElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in certElement.EnumerateArray())
                {
                    var cert = NormalizeFingerprint(item.ValueKind == JsonValueKind.String ? item.GetString() : null);
                    if (cert is null)
                    {
                        return Fail("signingCertSha256 entries must be SHA-256 fingerprints");
                    }

                    certs.Add(cert);
                }
            }

            var notes = (Str(root, "releaseNotes") ?? string.Empty).Replace("\r\n", "\n").Trim();
            if (notes.Length > MaxNotesLength)
            {
                notes = notes[..MaxNotesLength].TrimEnd() + "…";
            }

            DateTimeOffset? published = DateTimeOffset.TryParse(Str(root, "publishedAt"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var p) ? p : null;

            return new ManifestParseResult(
                new UpdateManifest(package!, versionName.Trim(), code, min, apkUrl, sha, size, certs, notes, published), null);
        }
        catch (JsonException ex)
        {
            return Fail($"manifest is not valid JSON ({ex.Message})");
        }
    }

    /// <summary>"AB:CD:..." or "abcd..." → 64 lower-case hex characters, else null.</summary>
    public static string? NormalizeFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hex = value.Replace(":", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
        return IsHex(hex, 64) ? hex : null;
    }

    private static bool IsHex(string value, int length) =>
        value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static bool TryInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out value);
    }

    private static ManifestParseResult Fail(string error) => new(null, error);
}
