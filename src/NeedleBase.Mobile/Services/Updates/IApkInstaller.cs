namespace NeedleBase.Mobile.Services.Updates;

/// <summary>What Android reports about a downloaded APK (null fields when it can't tell).</summary>
public sealed record ApkInfo(string? PackageName, long VersionCode, IReadOnlyList<string> SignerSha256);

/// <summary>Platform part of the updater (Android: Platforms/Android/AndroidApkInstaller.cs). Not supported elsewhere.</summary>
public interface IApkInstaller
{
    /// <summary>False on Play builds (updater compiled out) and non-Android targets.</summary>
    bool IsSupported { get; }
    string PackageName { get; }
    long CurrentVersionCode { get; }
    /// <summary>Where downloads go (app-private cache; exposed to the installer only through the FileProvider).</summary>
    string DownloadDirectory { get; }
    /// <summary>SHA-256 fingerprints of the certificate(s) this installed app is signed with.</summary>
    IReadOnlyList<string> OwnSignerSha256 { get; }
    /// <summary>"Install unknown apps" allowed for NeedleBase (Android 8+); always true before Android 8.</summary>
    bool CanInstallPackages { get; }
    /// <summary>Opens the "Install unknown apps" screen for NeedleBase.</summary>
    void OpenInstallPermissionSettings();
    ApkInfo? Inspect(string apkPath);
    /// <summary>Hands the verified APK to the system installer (the user confirms; Android checks the signature again).</summary>
    void Install(string apkPath);
}

/// <summary>Used where in-app updates don't exist (tests' default, Play builds without the Android installer).</summary>
public sealed class UnsupportedApkInstaller : IApkInstaller
{
    public bool IsSupported => false;
    public string PackageName => string.Empty;
    public long CurrentVersionCode => 0;
    public string DownloadDirectory => Path.GetTempPath();
    public IReadOnlyList<string> OwnSignerSha256 => [];
    public bool CanInstallPackages => false;
    public void OpenInstallPermissionSettings() { }
    public ApkInfo? Inspect(string apkPath) => null;
    public void Install(string apkPath) => throw new PlatformNotSupportedException();
}

/// <summary>Checks a downloaded APK against the manifest and the running app before it is offered to Android.</summary>
public static class ApkVerifier
{
    /// <returns>null when it may be installed, else the reason (shown to the user).</returns>
    public static string? Check(ApkInfo? info, UpdateManifest manifest, string ownPackage, long currentVersionCode, IReadOnlyList<string> ownSigners)
    {
        if (info is null || info.PackageName is null)
        {
            return "The downloaded file is not a valid NeedleBase app.";
        }

        if (!string.Equals(info.PackageName, ownPackage, StringComparison.Ordinal))
        {
            return "The downloaded file is a different app and was not installed.";
        }

        if (info.VersionCode != manifest.VersionCode || info.VersionCode <= currentVersionCode)
        {
            return "The downloaded file is not the expected newer version.";
        }

        var apkSigners = Normalize(info.SignerSha256);
        var own = Normalize(ownSigners);
        if (apkSigners.Count > 0 && own.Count > 0 && !apkSigners.Overlaps(own))
        {
            return "The update is signed with a different key than this app, so Android would refuse it. Download it from the NeedleBase page instead.";
        }

        var published = Normalize(manifest.SigningCertSha256);
        if (apkSigners.Count > 0 && published.Count > 0 && !apkSigners.Overlaps(published))
        {
            return "The update's signature doesn't match the published one and was not installed.";
        }

        return null;
    }

    private static HashSet<string> Normalize(IEnumerable<string> fingerprints) =>
        fingerprints.Select(UpdateManifestParser.NormalizeFingerprint).OfType<string>().ToHashSet(StringComparer.Ordinal);
}
