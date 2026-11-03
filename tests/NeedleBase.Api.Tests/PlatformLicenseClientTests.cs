using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Licensing;
using NeedleBase.Infrastructure.Licensing;

namespace NeedleBase.Api.Tests;

/// <summary>Token verification and the Platform HTTP client against a fake Platform (no network).</summary>
public sealed class PlatformLicenseClientTests : IDisposable
{
    private const string Shop = "01JSHOPLICENSECLIENT0000001";
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose() => _key.Dispose();

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private string Token(Dictionary<string, object?> claims, string kid = "k1", ECDsa? signer = null, string alg = "ES256")
    {
        var h = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg, kid, typ = "JWT" }));
        var p = B64(JsonSerializer.SerializeToUtf8Bytes(claims));
        var sig = (signer ?? _key).SignData(Encoding.ASCII.GetBytes(h + "." + p), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return h + "." + p + "." + B64(sig);
    }

    private static Dictionary<string, object?> Claims(Action<Dictionary<string, object?>>? tweak = null)
    {
        var c = new Dictionary<string, object?>
        {
            ["aud"] = "NEEDLEBASE", ["device_id"] = Shop, ["license_id"] = "lic-1", ["plan"] = "Pro",
            ["trial"] = true, ["exp"] = Now.AddDays(3).ToUnixTimeSeconds(), ["refresh_after"] = Now.AddHours(12).ToUnixTimeSeconds()
        };
        tweak?.Invoke(c);
        return c;
    }

    private IReadOnlyDictionary<string, ECParameters> Keys() =>
        new Dictionary<string, ECParameters> { ["k1"] = _key.ExportParameters(false) };

    private static readonly LicenseTokenVerifier.Expectation Expect = new("NEEDLEBASE", Shop, null, "lic-1");

    [Fact]
    public void Verify_accepts_a_good_token_and_reads_claims()
    {
        var check = LicenseTokenVerifier.Verify(Token(Claims()), Keys(), Expect, Now, out var error, out _);
        Assert.Null(error);
        Assert.NotNull(check);
        Assert.True(check!.IsTrial);
        Assert.Equal("Pro", check.Plan);
        Assert.Equal(Now.AddDays(3), check.GraceUntilUtc);
    }

    [Theory]
    [InlineData("aud", "OTHER", "token_audience")]
    [InlineData("device_id", "01JSOMEOTHERSHOP00000000001", "token_device")]
    [InlineData("license_id", "lic-2", "token_license")]
    public void Verify_rejects_wrong_claims(string claim, string value, string expected)
    {
        LicenseTokenVerifier.Verify(Token(Claims(c => c[claim] = value)), Keys(), Expect, Now, out var error, out _);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Verify_rejects_expired_unknown_kid_wrong_signer_alg_and_garbage()
    {
        string? e;
        LicenseTokenVerifier.Verify(Token(Claims(c => c["exp"] = Now.AddMinutes(-6).ToUnixTimeSeconds())), Keys(), Expect, Now, out e, out _);
        Assert.Equal("token_expired", e);
        LicenseTokenVerifier.Verify(Token(Claims(), kid: "k2"), Keys(), Expect, Now, out e, out _);
        Assert.Equal("token_unknown_kid", e);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        LicenseTokenVerifier.Verify(Token(Claims(), signer: other), Keys(), Expect, Now, out e, out _);
        Assert.Equal("token_signature", e);
        LicenseTokenVerifier.Verify(Token(Claims(), alg: "none"), Keys(), Expect, Now, out e, out _);
        Assert.Equal("token_alg", e);
        LicenseTokenVerifier.Verify("a.b", Keys(), Expect, Now, out e, out _);
        Assert.Equal("token_malformed", e);
        LicenseTokenVerifier.Verify("%%%.%%%.%%%", Keys(), Expect, Now, out e, out _);
        Assert.Equal("token_malformed", e);
        LicenseTokenVerifier.Verify(null, Keys(), Expect, Now, out e, out _);
        Assert.Equal("token_missing", e);
    }

    [Fact]
    public void Verify_checks_issuer_only_when_configured()
    {
        var token = Token(Claims(c => c["iss"] = "platform"));
        LicenseTokenVerifier.Verify(token, Keys(), Expect with { Issuer = "other" }, Now, out var e, out _);
        Assert.Equal("token_issuer", e);
        Assert.NotNull(LicenseTokenVerifier.Verify(token, Keys(), Expect with { Issuer = "platform" }, Now, out _, out _));
    }

    // ---- client ----

    private sealed class FakePlatform(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string? LastBody { get; private set; }
        public string? LastIntegrationKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
                LastIntegrationKey = request.Headers.GetValues("X-Integration-Key").Single();
            }

            return respond(request);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class Monitor(PlatformOptions value) : IOptionsMonitor<PlatformOptions>
    {
        public PlatformOptions CurrentValue => value;
        public PlatformOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PlatformOptions, string?> listener) => null;
    }

    private static HttpResponseMessage JsonResponse(object body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private object Jwks()
    {
        var p = _key.ExportParameters(false);
        return new { keys = new[] { new { kty = "EC", crv = "P-256", kid = "k1", x = B64(p.Q.X!), y = B64(p.Q.Y!) } } };
    }

    private (PlatformLicenseClient Client, FakePlatform Fake) Client(Func<HttpRequestMessage, HttpResponseMessage> validate, string? baseUrl = "https://platform.test")
    {
        var fake = new FakePlatform(r => r.RequestUri!.AbsolutePath.EndsWith("/keys", StringComparison.Ordinal) ? JsonResponse(Jwks()) : validate(r));
        var options = new PlatformOptions { BaseUrl = baseUrl, IntegrationKey = "test-integration-key" };
        return (new PlatformLicenseClient(new HttpClient(fake), new Monitor(options), new PlatformJwksCache(), new FixedClock(),
            NullLogger<PlatformLicenseClient>.Instance), fake);
    }

    private static readonly LicenseValidateCall Call = new("NB-TEST-KEY", Shop, "Shop", "owner@example.com");

    [Fact]
    public async Task Valid_answer_with_good_token_is_judged_and_sends_the_email_and_shop_id()
    {
        var (client, fake) = Client(_ => JsonResponse(new { isValid = true, reason = "ok", licenseId = "lic-1", token = Token(Claims()), emailMatches = true }));
        var outcome = await client.ValidateAsync(Call);

        Assert.False(outcome.Unavailable);
        Assert.True(outcome.Check!.IsValid);
        Assert.True(outcome.EmailMatches);
        Assert.Null(LicenseErrors.Decide(outcome));
        Assert.Contains("\"email\":\"owner@example.com\"", fake.LastBody);
        Assert.Contains(Shop, fake.LastBody);
        Assert.Equal("test-integration-key", fake.LastIntegrationKey);
    }

    [Fact]
    public async Task Valid_answer_with_a_token_for_another_shop_is_not_trusted()
    {
        var (client, _) = Client(_ => JsonResponse(new { isValid = true, licenseId = "lic-1", token = Token(Claims(c => c["device_id"] = "x")), emailMatches = true }));
        var outcome = await client.ValidateAsync(Call);
        Assert.True(outcome.Unavailable);
        Assert.Equal("token_device", outcome.UnavailableReason);
    }

    [Fact]
    public async Task Valid_answer_without_email_match_is_a_mismatch()
    {
        var (client, _) = Client(_ => JsonResponse(new { isValid = true, licenseId = "lic-1", token = Token(Claims()) }));
        Assert.Equal(LicenseErrors.EmailMismatch, LicenseErrors.Decide(await client.ValidateAsync(Call)));
    }

    [Theory]
    [InlineData("expired", false)]
    [InlineData("device_limit", false)]
    [InlineData("invalid_integration_key", true)]
    [InlineData("wrong_service", true)]
    public async Task Not_valid_reasons(string reason, bool unavailable)
    {
        var (client, _) = Client(_ => JsonResponse(new { isValid = false, reason }));
        var outcome = await client.ValidateAsync(Call);
        Assert.Equal(unavailable, outcome.Unavailable);
        if (!unavailable)
        {
            Assert.Equal(reason, outcome.Check!.Reason);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "service_unavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "service_unavailable")]
    public async Task Busy_or_broken_platform_is_unavailable_with_retry(HttpStatusCode code, string reason)
    {
        var (client, _) = Client(_ => JsonResponse(new { reason = "busy", retryAfterSeconds = 120 }, code));
        var outcome = await client.ValidateAsync(Call);
        Assert.True(outcome.Unavailable);
        Assert.Equal(reason, outcome.UnavailableReason);
        Assert.Equal(TimeSpan.FromMinutes(2), outcome.RetryAfter);
    }

    [Fact]
    public async Task Network_failure_and_timeout_are_unavailable()
    {
        var (net, _) = Client(_ => throw new HttpRequestException("down"));
        Assert.Equal("network", (await net.ValidateAsync(Call)).UnavailableReason);

        var (slow, _) = Client(_ => throw new TaskCanceledException("timeout"));
        Assert.Equal("timeout", (await slow.ValidateAsync(Call)).UnavailableReason);
    }

    [Fact]
    public async Task Not_configured_never_calls_platform()
    {
        var (client, fake) = Client(_ => throw new InvalidOperationException("must not be called"), baseUrl: null);
        Assert.False(client.IsConfigured);
        Assert.Equal("not_configured", (await client.ValidateAsync(Call)).UnavailableReason);
        Assert.Empty(fake.Paths);
    }

    [Fact]
    public async Task Jwks_is_cached_between_checks()
    {
        var (client, fake) = Client(_ => JsonResponse(new { isValid = true, licenseId = "lic-1", token = Token(Claims()), emailMatches = true }));
        await client.ValidateAsync(Call);
        await client.ValidateAsync(Call);
        Assert.Equal(1, fake.Paths.Count(p => p.EndsWith("/keys", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Jwks_keeps_working_keys_when_a_refresh_fails_or_comes_back_empty()
    {
        var calls = 0;
        var fake = new FakePlatform(_ => ++calls switch
        {
            1 => JsonResponse(Jwks()),
            2 => throw new HttpRequestException("down"),
            _ => JsonResponse(new { keys = Array.Empty<object>() })
        });
        using var http = new HttpClient(fake) { BaseAddress = new Uri("https://platform.test/") };
        var cache = new PlatformJwksCache();

        Assert.Single(await cache.GetAsync(http, false, Now, CancellationToken.None));
        Assert.Single(await cache.GetAsync(http, false, Now.AddHours(2), CancellationToken.None)); // fetch fails
        Assert.Single(await cache.GetAsync(http, false, Now.AddHours(2).AddMinutes(1), CancellationToken.None)); // waits
        Assert.Equal(2, calls);
        Assert.Single(await cache.GetAsync(http, false, Now.AddHours(2).AddMinutes(6), CancellationToken.None)); // empty set
        Assert.Equal(3, calls);
    }
}
