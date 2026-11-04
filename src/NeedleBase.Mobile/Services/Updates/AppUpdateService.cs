using System.Net.Http.Headers;

namespace NeedleBase.Mobile.Services.Updates;

public sealed record UpdateCheckResult(UpdateKind Kind, UpdateManifest? Manifest, string? Error, bool FromCache = false);

public enum InstallOutcome
{
    /// <summary>Android's installer is open; the user confirms there.</summary>
    InstallerOpened,
    /// <summary>"Install unknown apps" is off; the settings screen was opened. Try again when the user returns.</summary>
    PermissionNeeded
}

/// <summary>
/// In-app updates for sideloaded builds (docs/android-release.md, "Updates"). Reads update.json from GitHub Pages,
/// at most every <see cref="UpdatePolicy.CheckInterval"/> automatically (always on "Check for updates"), downloads
/// with resume, verifies size + SHA-256 + package + versionCode + signing certificate, gives pending changes a short
/// chance to sync, then opens Android's installer. Never blocks or stops sync: local data stays in the app's
/// database across the update and syncs on the next start if it could not before.
/// </summary>
public sealed class AppUpdateService
{
    public const string HttpClientName = "Updates";
    internal const string LastCheckKey = "updates.last_check_utc";
    internal const string SkippedKey = "updates.skipped_version_code";
    internal const string CachedManifestKey = "updates.manifest_json";

    private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BeforeInstallTimeout = TimeSpan.FromSeconds(20);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IApkInstaller _installer;
    private readonly IPreferences _preferences;
    private readonly IConnectivity _connectivity;
    private readonly Func<DateTimeOffset> _now;
    private readonly Uri? _manifestUrl;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppUpdateService(IHttpClientFactory httpFactory, IApkInstaller installer, IPreferences preferences,
        IConnectivity connectivity, string? manifestUrl, Func<DateTimeOffset>? now = null)
    {
        _httpFactory = httpFactory;
        _installer = installer;
        _preferences = preferences;
        _connectivity = connectivity;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _manifestUrl = manifestUrl is not null && Uri.TryCreate(manifestUrl, UriKind.Absolute, out var u) ? u : null;
    }

    /// <summary>Best-effort push of pending changes before the app is replaced (set by MauiProgram to SyncNowAsync).</summary>
    public Func<CancellationToken, Task>? BeforeInstall { get; set; }

    /// <summary>Sideload build with a manifest URL on a platform that can install APKs.</summary>
    public bool IsEnabled => _manifestUrl is not null && _installer.IsSupported;

    public string CurrentVersionDescription => _installer.IsSupported ? $"build {_installer.CurrentVersionCode}" : string.Empty;

    /// <summary>
    /// Automatic (manual=false) or "Check for updates" (manual=true). Automatic checks inside the interval, or while
    /// offline, re-evaluate the last downloaded manifest so a required update is still enforced without network.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return new UpdateCheckResult(UpdateKind.None, null, "Updates come from Google Play for this version.");
        }

        if (!manual && !await _gate.WaitAsync(0, ct))
        {
            return new UpdateCheckResult(UpdateKind.None, null, null); // a check or download is already running
        }

        if (manual)
        {
            await _gate.WaitAsync(ct);
        }

        try
        {
            var due = UpdatePolicy.IsCheckDue(ReadLastCheck(), _now(), manual);
            if (!due || _connectivity.NetworkAccess != NetworkAccess.Internet)
            {
                var cached = ReadCachedManifest();
                var kind = UpdatePolicy.Evaluate(_installer.CurrentVersionCode, cached, ReadSkipped(), manual);
                if (!manual && kind == UpdateKind.Optional)
                {
                    kind = UpdateKind.None; // optional prompts only after a real check, not on every return to the app
                }

                var error = due ? "You're offline. Connect to the internet and try again." : null;
                return new UpdateCheckResult(kind, kind == UpdateKind.None ? null : cached, error, FromCache: true);
            }

            var (manifest, json, fetchError) = await FetchManifestAsync(ct);
            if (manifest is null)
            {
                // Keep enforcing a known required update even when the server can't be read.
                var cached = ReadCachedManifest();
                var kind = UpdatePolicy.Evaluate(_installer.CurrentVersionCode, cached, ReadSkipped(), manual);
                return new UpdateCheckResult(kind == UpdateKind.Required ? kind : UpdateKind.None,
                    kind == UpdateKind.Required ? cached : null, fetchError, FromCache: true);
            }

            _preferences.Set(CachedManifestKey, json!);
            _preferences.Set(LastCheckKey, _now().ToUnixTimeSeconds());
            CleanupOldDownloads(manifest);
            var result = UpdatePolicy.Evaluate(_installer.CurrentVersionCode, manifest, ReadSkipped(), manual);
            return new UpdateCheckResult(result, result == UpdateKind.None ? null : manifest, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>"Later" on an optional update: no automatic prompt for this version again.</summary>
    public void Skip(UpdateManifest manifest) => _preferences.Set(SkippedKey, manifest.VersionCode);

    public async Task<InstallOutcome> DownloadAndInstallAsync(UpdateManifest manifest, IProgress<double>? progress, CancellationToken ct)
    {
        if (!IsEnabled)
        {
            throw new UpdateException("Updates come from Google Play for this version.");
        }

        await _gate.WaitAsync(ct);
        try
        {
            // Ask for "Install unknown apps" first, so a big download isn't wasted on a refusal.
            if (!_installer.CanInstallPackages)
            {
                _installer.OpenInstallPermissionSettings();
                return InstallOutcome.PermissionNeeded;
            }

            if (_connectivity.NetworkAccess != NetworkAccess.Internet &&
                !File.Exists(UpdateDownloader.FinalPath(_installer.DownloadDirectory, manifest)))
            {
                throw new UpdateException("You're offline. Connect to the internet and try again.");
            }

            var downloader = new UpdateDownloader(_httpFactory.CreateClient(HttpClientName));
            var apk = await downloader.DownloadAsync(manifest, _installer.DownloadDirectory, progress, ct);

            var problem = ApkVerifier.Check(_installer.Inspect(apk), manifest, _installer.PackageName,
                _installer.CurrentVersionCode, _installer.OwnSignerSha256);
            if (problem is not null)
            {
                TryDelete(apk);
                throw new UpdateException(problem);
            }

            await SyncBeforeInstallAsync(ct);
            _installer.Install(apk);
            return InstallOutcome.InstallerOpened;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SyncBeforeInstallAsync(CancellationToken ct)
    {
        if (BeforeInstall is not { } before)
        {
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(BeforeInstallTimeout);
            await before(timeout.Token).WaitAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Never block the update on sync: unsynced changes stay in the local database and sync after the update.
            System.Diagnostics.Debug.WriteLine($"Updates: pre-install sync skipped: {ex.Message}");
        }
    }

    private async Task<(UpdateManifest? Manifest, string? Json, string? Error)> FetchManifestAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ManifestTimeout);
            // Cache-buster + no-cache: GitHub Pages' CDN caches for up to 10 minutes.
            var url = new UriBuilder(_manifestUrl!) { Query = $"t={_now().ToUnixTimeSeconds() / 60}" }.Uri;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await _httpFactory.CreateClient(HttpClientName).SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return (null, null, $"Couldn't check for updates ({(int)response.StatusCode}). Try again later.");
            }

            if (response.Content.Headers.ContentLength > 64 * 1024)
            {
                return (null, null, "The update information is too large.");
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var parsed = UpdateManifestParser.Parse(json, _manifestUrl!, _installer.PackageName);
            return parsed.Ok
                ? (parsed.Manifest, json, null)
                : (null, null, $"The update information is invalid ({parsed.Error}).");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, null, "Checking for updates timed out. Try again later.");
        }
        catch (HttpRequestException)
        {
            return (null, null, "Couldn't reach the update server. Try again later.");
        }
    }

    private UpdateManifest? ReadCachedManifest()
    {
        var json = _preferences.Get(CachedManifestKey, string.Empty);
        return string.IsNullOrEmpty(json) || _manifestUrl is null
            ? null
            : UpdateManifestParser.Parse(json, _manifestUrl, _installer.PackageName).Manifest;
    }

    private DateTimeOffset? ReadLastCheck()
    {
        var seconds = _preferences.Get(LastCheckKey, 0L);
        return seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }

    private int? ReadSkipped()
    {
        var code = _preferences.Get(SkippedKey, 0);
        return code > 0 ? code : null;
    }

    /// <summary>Removes downloads of other versions (and everything once this build is current) to free space.</summary>
    private void CleanupOldDownloads(UpdateManifest latest)
    {
        try
        {
            var dir = _installer.DownloadDirectory;
            if (!Directory.Exists(dir))
            {
                return;
            }

            var keep = latest.VersionCode > _installer.CurrentVersionCode ? Path.GetFileName(UpdateDownloader.FinalPath(dir, latest)) : null;
            foreach (var file in Directory.EnumerateFiles(dir, "needlebase-*.apk*"))
            {
                var name = Path.GetFileName(file);
                if (keep is null || (name != keep && name != keep + ".part"))
                {
                    TryDelete(file);
                }
            }
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Updates: cleanup failed: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
