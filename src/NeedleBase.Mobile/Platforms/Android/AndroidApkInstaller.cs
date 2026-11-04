using System.Security.Cryptography;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using NeedleBase.Mobile.Services.Updates;

#if NEEDLEBASE_OTA
// Sideload builds only (NeedleBaseUpdateChannel=sideload). Play builds never declare this permission (Play policy).
[assembly: Android.App.UsesPermission("android.permission.REQUEST_INSTALL_PACKAGES")]
#endif

namespace NeedleBase.Mobile;

#if NEEDLEBASE_OTA
/// <summary>Shares only cache/updates/ (Resources/xml/update_paths.xml) with the system installer.</summary>
[ContentProvider(new[] { AndroidApkInstaller.Authority }, Name = "com.helmutcode.needlebase.UpdateFileProvider",
    Exported = false, GrantUriPermissions = true)]
[MetaData("android.support.FILE_PROVIDER_PATHS", Resource = "@xml/update_paths")]
public sealed class UpdateFileProvider : AndroidX.Core.Content.FileProvider
{
}
#endif

/// <summary>Android side of in-app updates: permission, APK inspection (package, versionCode, signer) and install.</summary>
public sealed class AndroidApkInstaller : IApkInstaller
{
    public const string Authority = "com.helmutcode.needlebase.updates";

    private static Context Context => global::Android.App.Application.Context;

#if NEEDLEBASE_OTA
    public bool IsSupported => true;
#else
    public bool IsSupported => false;
#endif

    public string PackageName => Context.PackageName ?? string.Empty;

    public long CurrentVersionCode
    {
        get
        {
            var info = GetPackageInfo(PackageName, 0);
            if (info is null) return 0;
#pragma warning disable CA1416, CA1422
            return OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : info.VersionCode;
#pragma warning restore CA1416, CA1422
        }
    }

    // Android 7+: private cache shared through the FileProvider. Android 5-6's installer can't open content:// URIs,
    // so there the APK goes to the app's external cache and is passed as file:// (the platform's own way then).
    public string DownloadDirectory => Path.Combine(
        (UseFileUri ? Context.ExternalCacheDir ?? Context.CacheDir : Context.CacheDir)!.AbsolutePath, "updates");

    private static bool UseFileUri => !OperatingSystem.IsAndroidVersionAtLeast(24);

    public IReadOnlyList<string> OwnSignerSha256 => Signers(GetPackageInfo(PackageName, SignerFlags), includeHistory: true);

    public bool CanInstallPackages =>
        !OperatingSystem.IsAndroidVersionAtLeast(26) || Context.PackageManager!.CanRequestPackageInstalls();

    public void OpenInstallPermissionSettings()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var intent = new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
            global::Android.Net.Uri.Parse("package:" + PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        Context.StartActivity(intent);
    }

    public ApkInfo? Inspect(string apkPath)
    {
        var pm = Context.PackageManager!;
#pragma warning disable CA1416, CA1422
        var info = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? pm.GetPackageArchiveInfo(apkPath, PackageManager.PackageInfoFlags.Of((long)SignerFlags))
            : pm.GetPackageArchiveInfo(apkPath, SignerFlags);
        if (info is null) return null;
        var code = OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : info.VersionCode;
#pragma warning restore CA1416, CA1422
        var signers = Signers(info, includeHistory: false);
        if (signers.Count == 0 && !OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // Some Android 9-12 builds leave SigningInfo empty for archives; the legacy field still works there.
#pragma warning disable CA1416, CA1422, CS0618
            var legacy = pm.GetPackageArchiveInfo(apkPath, PackageInfoFlags.Signatures);
#pragma warning restore CA1416, CA1422, CS0618
            signers = Signers(legacy, includeHistory: false);
        }

        return new ApkInfo(info.PackageName, code, signers);
    }

    public void Install(string apkPath)
    {
        var file = new Java.IO.File(apkPath);
        var uri = UseFileUri
            ? global::Android.Net.Uri.FromFile(file)!
            : AndroidX.Core.Content.FileProvider.GetUriForFile(Context, Authority, file);
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        Context.StartActivity(intent);
    }

#pragma warning disable CA1416, CA1422, CS0618
    private static PackageInfoFlags SignerFlags =>
        OperatingSystem.IsAndroidVersionAtLeast(28) ? PackageInfoFlags.SigningCertificates : PackageInfoFlags.Signatures;

    private static PackageInfo? GetPackageInfo(string package, PackageInfoFlags flags)
    {
        try
        {
            var pm = Context.PackageManager!;
            return OperatingSystem.IsAndroidVersionAtLeast(33)
                ? pm.GetPackageInfo(package, PackageManager.PackageInfoFlags.Of((long)flags))
                : pm.GetPackageInfo(package, flags);
        }
        catch (PackageManager.NameNotFoundException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> Signers(PackageInfo? info, bool includeHistory)
    {
        if (info is null) return [];
        var signatures = new List<Signature>();
        if (OperatingSystem.IsAndroidVersionAtLeast(28) && info.SigningInfo is { } signing)
        {
            if (signing.HasMultipleSigners)
            {
                signatures.AddRange(signing.GetApkContentsSigners() ?? []);
            }
            else
            {
                // History lists the current key plus any rotated-away keys (APK Signature Scheme v3).
                var history = signing.GetSigningCertificateHistory() ?? [];
                signatures.AddRange(includeHistory ? history : history.TakeLast(1)); // last = current key
            }
        }

        if (signatures.Count == 0 && info.Signatures is { } legacy)
        {
            signatures.AddRange(legacy);
        }

        return signatures
            .Select(s => s.ToByteArray())
            .OfType<byte[]>()
            .Select(bytes => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant())
            .Distinct()
            .ToList();
    }
#pragma warning restore CA1416, CA1422, CS0618
}
