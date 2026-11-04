using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Services.Updates;

namespace NeedleBase.Mobile.Tests;

/// <summary>In-app updates (docs/android-release.md "Updates"): manifest, policy/throttle, download + hash, APK checks, service.</summary>
public sealed class AppUpdateTests : IDisposable
{
    private const string Package = "com.helmutcode.needlebase";
    private static readonly Uri ManifestUrl = new("https://helmut-essien.github.io/NeedleBase/update.json");
    private static readonly string CertA = new('a', 64);
    private static readonly string CertB = new('b', 64);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nb-update-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static string Json(int code = 42, int min = 0, string? sha = null, long size = 1000, string apk = "apk/needlebase-1.2.0-42.apk",
        string package = Package, int schema = 1, string certs = "[\"AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA\"]") =>
        $$"""
        {"schema":{{schema}},"packageName":"{{package}}","versionName":"1.2.0","versionCode":{{code}},"minSupportedVersionCode":{{min}},
         "apkUrl":"{{apk}}","sha256":"{{sha ?? new string('0', 64)}}","size":{{size}},"signingCertSha256":{{certs}},
         "releaseNotes":"Faster sync","publishedAt":"2026-11-04T10:00:00Z"}
        """;

    private static UpdateManifest Manifest(byte[] apk, int code = 42, int min = 0) =>
        UpdateManifestParser.Parse(Json(code, min, Convert.ToHexString(SHA256.HashData(apk)).ToLowerInvariant(), apk.Length), ManifestUrl, Package).Manifest!;

    // ---------- manifest parsing ----------

    [Fact]
    public void Parse_ValidManifest_ResolvesRelativeApkUrlAndNormalisesCert()
    {
        var result = UpdateManifestParser.Parse(Json(), ManifestUrl, Package);
        Assert.True(result.Ok, result.Error);
        var m = result.Manifest!;
        Assert.Equal("https://helmut-essien.github.io/NeedleBase/apk/needlebase-1.2.0-42.apk", m.ApkUrl.ToString());
        Assert.Equal(42, m.VersionCode);
        Assert.Equal(CertA, Assert.Single(m.SigningCertSha256));
        Assert.Equal("Faster sync", m.ReleaseNotes);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero), m.PublishedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Parse_Garbage_Fails(string json) => Assert.False(UpdateManifestParser.Parse(json, ManifestUrl, Package).Ok);

    [Fact]
    public void Parse_RejectsWrongSchemaPackageHttpBadHashSizeAndMin()
    {
        Assert.False(UpdateManifestParser.Parse(Json(schema: 2), ManifestUrl, Package).Ok);
        Assert.False(UpdateManifestParser.Parse(Json(package: "com.evil.app"), ManifestUrl, Package).Ok);
        Assert.False(UpdateManifestParser.Parse(Json(apk: "http://example.com/a.apk"), ManifestUrl, Package).Ok);
        Assert.False(UpdateManifestParser.Parse(Json(sha: "abc"), ManifestUrl, Package).Ok);
        Assert.False(UpdateManifestParser.Parse(Json(size: 0), ManifestUrl, Package).Ok);
        Assert.False(UpdateManifestParser.Parse(Json(size: UpdateManifestParser.MaxApkBytes + 1), ManifestUrl, Package).Ok);
        Assert.False(UpdateManifestParser.Parse(Json(code: 0), ManifestUrl, Package).Ok);
        Assert.False(UpdateManifestParser.Parse(Json(code: 5, min: 6), ManifestUrl, Package).Ok);
        Assert.False(UpdateManifestParser.Parse(Json(certs: "[\"nope\"]"), ManifestUrl, Package).Ok);
    }

    [Fact]
    public void Parse_LongNotesAreCapped()
    {
        var json = Json().Replace("Faster sync", new string('x', 5000));
        Assert.True(UpdateManifestParser.Parse(json, ManifestUrl, Package).Manifest!.ReleaseNotes.Length <= UpdateManifestParser.MaxNotesLength + 1);
    }

    [Theory]
    [InlineData("sideload", "https://x.github.io/NeedleBase/update.json", true)]
    [InlineData("SIDELOAD", "https://x.github.io/NeedleBase/update.json", true)]
    [InlineData("play", "https://x.github.io/NeedleBase/update.json", false)]
    [InlineData("sideload", "http://x.github.io/update.json", false)]
    [InlineData("sideload", "https://updates.placeholder.invalid/update.json", false)]
    [InlineData("sideload", "", false)]
    [InlineData(null, "https://x.github.io/update.json", false)]
    public void Endpoint_OnlySideloadWithHttpsUrl(string? channel, string url, bool enabled) =>
        Assert.Equal(enabled, UpdateEndpoint.Resolve(channel, url) is not null);

    [Fact]
    public void Endpoint_TestAssemblyHasNoUpdateUrl() => Assert.Null(UpdateEndpoint.ManifestUrl(typeof(AppUpdateTests).Assembly));

    // ---------- version comparison / policy / throttle ----------

    [Theory]
    [InlineData(42, 0, null, false, UpdateKind.None)]      // same version
    [InlineData(50, 0, null, false, UpdateKind.None)]      // newer than published (downgrade never offered)
    [InlineData(41, 0, null, false, UpdateKind.Optional)]
    [InlineData(41, 0, 42, false, UpdateKind.None)]        // "Later" for this version
    [InlineData(41, 0, 42, true, UpdateKind.Optional)]     // ...but a manual check still offers it
    [InlineData(41, 0, 40, false, UpdateKind.Optional)]    // skipped an older version only
    [InlineData(9, 10, 42, false, UpdateKind.Required)]    // below minimum: skip ignored
    [InlineData(10, 10, null, false, UpdateKind.Optional)] // exactly the minimum is still supported
    public void Policy_Evaluate(long current, int min, int? skipped, bool manual, UpdateKind expected)
    {
        var m = UpdateManifestParser.Parse(Json(42, min), ManifestUrl, Package).Manifest!;
        Assert.Equal(expected, UpdatePolicy.Evaluate(current, m, skipped, manual));
    }

    [Fact]
    public void Policy_Throttle()
    {
        var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        Assert.True(UpdatePolicy.IsCheckDue(null, now, manual: false));
        Assert.False(UpdatePolicy.IsCheckDue(now.AddHours(-1), now, manual: false));
        Assert.True(UpdatePolicy.IsCheckDue(now.AddHours(-1), now, manual: true));
        Assert.True(UpdatePolicy.IsCheckDue(now - UpdatePolicy.CheckInterval, now, manual: false));
        Assert.True(UpdatePolicy.IsCheckDue(now.AddDays(3), now, manual: false)); // clock moved back
    }

    // ---------- download, resume, hash ----------

    [Fact]
    public async Task Download_FullFile_VerifiesHashAndRenames()
    {
        var apk = RandomBytes(300_000);
        var m = Manifest(apk);
        var reports = new List<double>();
        var path = await new UpdateDownloader(new HttpClient(new FileServer(apk))).DownloadAsync(m, _dir, new SyncProgress(reports.Add), default);
        Assert.Equal(UpdateDownloader.FinalPath(_dir, m), path);
        Assert.Equal(apk, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.Equal(1, reports[^1]);
        Assert.InRange(reports.Count, 2, 102); // throttled to about every 1 %
    }

    [Fact]
    public async Task Download_ResumesFromPartWithRange()
    {
        var apk = RandomBytes(100_000);
        var m = Manifest(apk);
        Directory.CreateDirectory(_dir);
        await File.WriteAllBytesAsync(UpdateDownloader.FinalPath(_dir, m) + ".part", apk[..40_000]);
        var server = new FileServer(apk);
        var path = await new UpdateDownloader(new HttpClient(server)).DownloadAsync(m, _dir, null, default);
        Assert.Equal(40_000, server.LastRangeFrom);
        Assert.Equal(apk, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Download_ServerIgnoringRange_RestartsCleanly()
    {
        var apk = RandomBytes(50_000);
        var m = Manifest(apk);
        Directory.CreateDirectory(_dir);
        await File.WriteAllBytesAsync(UpdateDownloader.FinalPath(_dir, m) + ".part", RandomBytes(10_000));
        var path = await new UpdateDownloader(new HttpClient(new FileServer(apk) { SupportsRange = false })).DownloadAsync(m, _dir, null, default);
        Assert.Equal(apk, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Download_416_DeletesPart()
    {
        var apk = RandomBytes(10_000);
        var m = Manifest(apk);
        Directory.CreateDirectory(_dir);
        var part = UpdateDownloader.FinalPath(_dir, m) + ".part";
        await File.WriteAllBytesAsync(part, RandomBytes(5_000));
        await Assert.ThrowsAsync<UpdateException>(() =>
            new UpdateDownloader(new HttpClient(new FileServer(apk) { Force416 = true })).DownloadAsync(m, _dir, null, default));
        Assert.False(File.Exists(part));
    }

    [Fact]
    public async Task Download_HashMismatch_DeletesAndNeverProducesFinalFile()
    {
        var apk = RandomBytes(20_000);
        var m = Manifest(apk);
        var tampered = (byte[])apk.Clone();
        tampered[100] ^= 0xFF;
        var ex = await Assert.ThrowsAsync<UpdateException>(() =>
            new UpdateDownloader(new HttpClient(new FileServer(tampered))).DownloadAsync(m, _dir, null, default));
        Assert.Contains("security check", ex.Message);
        Assert.Empty(Directory.EnumerateFiles(_dir));
    }

    [Fact]
    public async Task Download_Oversize_StopsAndDeletes()
    {
        var apk = RandomBytes(20_000);
        var m = Manifest(apk);
        await Assert.ThrowsAsync<UpdateException>(() =>
            new UpdateDownloader(new HttpClient(new FileServer([.. apk, .. RandomBytes(5_000)]))).DownloadAsync(m, _dir, null, default));
        Assert.Empty(Directory.EnumerateFiles(_dir));
    }

    [Fact]
    public async Task Download_StalledStream_FailsAndKeepsPartForResume()
    {
        var apk = RandomBytes(10_000);
        var m = Manifest(apk);
        var ex = await Assert.ThrowsAsync<UpdateException>(() =>
            new UpdateDownloader(new HttpClient(new StallServer(apk[..4_000])), TimeSpan.FromMilliseconds(200)).DownloadAsync(m, _dir, null, default));
        Assert.Contains("stalled", ex.Message);
        Assert.Equal(4_000, new FileInfo(UpdateDownloader.FinalPath(_dir, m) + ".part").Length);
    }

    [Fact]
    public async Task Download_ExistingVerifiedFile_IsReusedWithoutNetwork()
    {
        var apk = RandomBytes(5_000);
        var m = Manifest(apk);
        Directory.CreateDirectory(_dir);
        await File.WriteAllBytesAsync(UpdateDownloader.FinalPath(_dir, m), apk);
        var server = new FileServer(apk);
        await new UpdateDownloader(new HttpClient(server)).DownloadAsync(m, _dir, null, default);
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public async Task HashMatches_IsCaseInsensitiveAndRejectsOthers()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "f");
        await File.WriteAllBytesAsync(file, Encoding.UTF8.GetBytes("needlebase"));
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("needlebase")));
        Assert.True(await UpdateDownloader.HashMatchesAsync(file, hex.ToUpperInvariant(), default));
        Assert.True(await UpdateDownloader.HashMatchesAsync(file, hex.ToLowerInvariant(), default));
        Assert.False(await UpdateDownloader.HashMatchesAsync(file, new string('0', 64), default));
    }

    // ---------- APK checks ----------

    [Fact]
    public void Verifier_ChecksPackageVersionAndSigners()
    {
        var m = Manifest(RandomBytes(10)); // publishes CertA
        Assert.Null(ApkVerifier.Check(new ApkInfo(Package, 42, [CertA]), m, Package, 41, [CertA]));
        Assert.NotNull(ApkVerifier.Check(null, m, Package, 41, [CertA]));
        Assert.NotNull(ApkVerifier.Check(new ApkInfo("com.other", 42, [CertA]), m, Package, 41, [CertA]));
        Assert.NotNull(ApkVerifier.Check(new ApkInfo(Package, 43, [CertA]), m, Package, 41, [CertA])); // not the manifest version
        Assert.NotNull(ApkVerifier.Check(new ApkInfo(Package, 42, [CertA]), m, Package, 42, [CertA])); // not newer
        Assert.NotNull(ApkVerifier.Check(new ApkInfo(Package, 42, [CertB]), m, Package, 41, [CertB])); // not the published key
        Assert.NotNull(ApkVerifier.Check(new ApkInfo(Package, 42, [CertA]), m, Package, 41, [CertB])); // Play-signed install
        // Rotated key: own history contains the old key.
        Assert.Null(ApkVerifier.Check(new ApkInfo(Package, 42, [CertA]), m, Package, 41, [CertB, CertA]));
    }

    // ---------- service ----------

    [Fact]
    public async Task Service_DisabledWithoutUrlOrInstaller()
    {
        var svc = Service(new FileServer([]), new FakeInstaller(), manifestUrl: null);
        Assert.False(svc.IsEnabled);
        Assert.Equal(UpdateKind.None, (await svc.CheckAsync(manual: true)).Kind);
        Assert.False(Service(new FileServer([]), new FakeInstaller { IsSupported = false }).IsEnabled);
    }

    [Fact]
    public async Task Service_ThrottlesAndCachesManifest()
    {
        var prefs = new MemoryPreferences();
        var server = new ManifestServer(Json(42));
        var now = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var svc = Service(server, new FakeInstaller(), prefs: prefs, now: () => now);

        Assert.Equal(UpdateKind.Optional, (await svc.CheckAsync(manual: false)).Kind);
        Assert.Equal(1, server.Requests);
        Assert.Contains("t=", server.LastQuery);

        now = now.AddHours(1);
        var throttled = await svc.CheckAsync(manual: false);
        Assert.Equal(1, server.Requests);                 // no network inside the interval
        Assert.Equal(UpdateKind.None, throttled.Kind);    // optional isn't re-prompted from cache

        Assert.Equal(UpdateKind.Optional, (await svc.CheckAsync(manual: true)).Kind);
        Assert.Equal(2, server.Requests);                 // manual always checks
    }

    [Fact]
    public async Task Service_RequiredIsEnforcedFromCacheWhenOfflineOrServerDown()
    {
        var prefs = new MemoryPreferences();
        var installer = new FakeInstaller { CurrentVersionCode = 5 };
        var server = new ManifestServer(Json(42, min: 10));
        var connectivity = new FakeConnectivity();
        var svc = Service(server, installer, prefs: prefs, connectivity: connectivity);
        Assert.Equal(UpdateKind.Required, (await svc.CheckAsync(manual: false)).Kind);

        connectivity.Access = NetworkAccess.None;
        Assert.Equal(UpdateKind.Required, (await svc.CheckAsync(manual: true)).Kind);

        connectivity.Access = NetworkAccess.Internet;
        server.Status = HttpStatusCode.ServiceUnavailable;
        var down = await svc.CheckAsync(manual: true);
        Assert.Equal(UpdateKind.Required, down.Kind);
        Assert.NotNull(down.Error);
    }

    [Fact]
    public async Task Service_BadManifestOrServerError_ReportsErrorWithoutThrowing()
    {
        var server = new ManifestServer("{\"schema\":1}");
        var result = await Service(server, new FakeInstaller()).CheckAsync(manual: true);
        Assert.Equal(UpdateKind.None, result.Kind);
        Assert.Contains("invalid", result.Error);

        server.Throw = true;
        Assert.Contains("reach", (await Service(server, new FakeInstaller()).CheckAsync(manual: true)).Error);
    }

    [Fact]
    public async Task Service_SkipSuppressesAutomaticPromptOnly()
    {
        var prefs = new MemoryPreferences();
        var svc = Service(new ManifestServer(Json(42)), new FakeInstaller(), prefs: prefs);
        var first = await svc.CheckAsync(manual: false);
        svc.Skip(first.Manifest!);
        prefs.Remove("updates.last_check_utc");
        Assert.Equal(UpdateKind.None, (await svc.CheckAsync(manual: false)).Kind);
        Assert.Equal(UpdateKind.Optional, (await svc.CheckAsync(manual: true)).Kind);
    }

    [Fact]
    public async Task Service_Install_AsksPermissionFirst()
    {
        var installer = new FakeInstaller { CanInstallPackages = false };
        var apk = RandomBytes(1000);
        var server = new FileServer(apk);
        var svc = Service(server, installer, dir: _dir);
        Assert.Equal(InstallOutcome.PermissionNeeded, await svc.DownloadAndInstallAsync(Manifest(apk), null, default));
        Assert.True(installer.SettingsOpened);
        Assert.Equal(0, server.Requests); // nothing downloaded before permission
    }

    [Fact]
    public async Task Service_Install_VerifiesSyncsThenOpensInstaller()
    {
        var apk = RandomBytes(4000);
        var installer = new FakeInstaller { Inspected = new ApkInfo(Package, 42, [CertA]) };
        var order = new List<string>();
        installer.OnInstall = () => order.Add("install");
        var svc = Service(new FileServer(apk), installer, dir: _dir);
        svc.BeforeInstall = _ => { order.Add("sync"); return Task.CompletedTask; };
        Assert.Equal(InstallOutcome.InstallerOpened, await svc.DownloadAndInstallAsync(Manifest(apk), null, default));
        Assert.Equal(["sync", "install"], order);
        Assert.Equal(UpdateDownloader.FinalPath(_dir, Manifest(apk)), installer.InstalledPath);
    }

    [Fact]
    public async Task Service_Install_FailingOrHangingSyncNeverBlocks()
    {
        var apk = RandomBytes(4000);
        var installer = new FakeInstaller { Inspected = new ApkInfo(Package, 42, [CertA]) };
        var svc = Service(new FileServer(apk), installer, dir: _dir);
        svc.BeforeInstall = _ => throw new InvalidOperationException("offline");
        Assert.Equal(InstallOutcome.InstallerOpened, await svc.DownloadAndInstallAsync(Manifest(apk), null, default));
    }

    [Fact]
    public async Task Service_Install_WrongSignerIsDeletedAndRefused()
    {
        var apk = RandomBytes(4000);
        var installer = new FakeInstaller { Inspected = new ApkInfo(Package, 42, [CertB]), OwnSignerSha256 = [CertB] };
        var svc = Service(new FileServer(apk), installer, dir: _dir);
        await Assert.ThrowsAsync<UpdateException>(() => svc.DownloadAndInstallAsync(Manifest(apk), null, default));
        Assert.Null(installer.InstalledPath);
        Assert.Empty(Directory.EnumerateFiles(_dir));
    }

    [Fact]
    public async Task Service_Install_OfflineFailsClearly()
    {
        var apk = RandomBytes(4000);
        var svc = Service(new FileServer(apk), new FakeInstaller(), dir: _dir, connectivity: new FakeConnectivity { Access = NetworkAccess.None });
        var ex = await Assert.ThrowsAsync<UpdateException>(() => svc.DownloadAndInstallAsync(Manifest(apk), null, default));
        Assert.Contains("offline", ex.Message);
    }

    // ---------- helpers ----------

    private AppUpdateService Service(HttpMessageHandler handler, FakeInstaller installer, string? manifestUrl = "https://helmut-essien.github.io/NeedleBase/update.json",
        MemoryPreferences? prefs = null, FakeConnectivity? connectivity = null, Func<DateTimeOffset>? now = null, string? dir = null)
    {
        installer.DownloadDirectory = dir ?? _dir;
        return new AppUpdateService(new StubFactory(handler), installer, prefs ?? new MemoryPreferences(), connectivity ?? new FakeConnectivity(), manifestUrl, now);
    }

    private static byte[] RandomBytes(int n) => RandomNumberGenerator.GetBytes(n);

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class FileServer(byte[] content) : HttpMessageHandler
    {
        public bool SupportsRange { get; init; } = true;
        public bool Force416 { get; init; }
        public long? LastRangeFrom { get; private set; }
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Force416)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
            }

            var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From;
            LastRangeFrom = from;
            if (SupportsRange && from is > 0 and var start && start < content.Length)
            {
                var partial = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(content[(int)start..]) };
                partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, content.Length - 1, content.Length);
                return Task.FromResult(partial);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }

    /// <summary>Sends the first bytes, then never another (a dead connection that stays open).</summary>
    private sealed class StallServer(byte[] first) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallStream(first)) });
    }

    private sealed class StallStream(byte[] first) : Stream
    {
        private int _pos;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pos < first.Length)
            {
                var n = Math.Min(buffer.Length, first.Length - _pos);
                first.AsMemory(_pos, n).CopyTo(buffer);
                _pos += n;
                return n;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private sealed class ManifestServer(string json) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool Throw { get; set; }
        public int Requests { get; private set; }
        public string LastQuery { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throw) throw new HttpRequestException("no route");
            Requests++;
            LastQuery = request.RequestUri!.Query;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(json) });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeConnectivity : IConnectivity
    {
        public NetworkAccess Access { get; set; } = NetworkAccess.Internet;
        public IEnumerable<ConnectionProfile> ConnectionProfiles => [ConnectionProfile.WiFi];
        public NetworkAccess NetworkAccess => Access;
        public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged { add { } remove { } }
    }

    private sealed class MemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, object?> _values = new();
        public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey(key);
        public void Remove(string key, string? sharedName = null) => _values.Remove(key);
        public void Clear(string? sharedName = null) => _values.Clear();
        public void Set<T>(string key, T value, string? sharedName = null) => _values[key] = value;
        public T Get<T>(string key, T defaultValue, string? sharedName = null) => _values.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
    }

    private sealed class FakeInstaller : IApkInstaller
    {
        public bool IsSupported { get; set; } = true;
        public string PackageName => Package;
        public long CurrentVersionCode { get; set; } = 41;
        public string DownloadDirectory { get; set; } = Path.GetTempPath();
        public IReadOnlyList<string> OwnSignerSha256 { get; set; } = [CertA];
        public bool CanInstallPackages { get; set; } = true;
        public bool SettingsOpened { get; private set; }
        public ApkInfo? Inspected { get; set; } = new(Package, 42, [CertA]);
        public string? InstalledPath { get; private set; }
        public Action? OnInstall { get; set; }
        public void OpenInstallPermissionSettings() => SettingsOpened = true;
        public ApkInfo? Inspect(string apkPath) => Inspected;
        public void Install(string apkPath) { InstalledPath = apkPath; OnInstall?.Invoke(); }
    }
}
