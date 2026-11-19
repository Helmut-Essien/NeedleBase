namespace NeedleBase.Web.Downloads;

/// <summary>One published Android APK from a GitHub Release.</summary>
public sealed record AndroidBuild(
    string Version,
    string Tag,
    string FileName,
    string DownloadUrl,
    long? SizeBytes,
    string? ReleaseUrl);

/// <summary>
/// Latest stable and pre-release (beta) APKs for the /download page.
/// When <see cref="LookupFailed"/> is true, the GitHub lookup failed and the page should
/// fall back to the releases list rather than invent a file.
/// </summary>
public sealed record AndroidDownloadLinks(
    AndroidBuild? Stable,
    AndroidBuild? Beta,
    bool LookupFailed,
    string ReleasesUrl,
    string LatestStablePageUrl);

/// <summary>Looks up the current stable and beta Android APKs (cached).</summary>
public interface IAndroidReleaseCatalog
{
    Task<AndroidDownloadLinks> GetAsync(CancellationToken cancellationToken = default);
}
