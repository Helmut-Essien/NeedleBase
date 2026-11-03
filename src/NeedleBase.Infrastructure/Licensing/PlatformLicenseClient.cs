using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Infrastructure.Licensing;

/// <summary>
/// HTTP client for Platform's <c>POST /api/licenses/validate</c> (header X-Integration-Key). Contract: 200 = judged
/// (isValid + reason), 400 = bad request, 429/503 = not judged (Retry-After). Anything that isn't a definite,
/// verified answer about the key itself (network, timeout, 429, 5xx, our own integration-key/service-code problem,
/// missing or bad token) is "unavailable" and never locks a shop. Never logs the license key or integration key.
/// </summary>
public sealed class PlatformLicenseClient : ILicensePlatform
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Platform reasons that judge the key itself; everything else is not the shop's fault.</summary>
    private static readonly HashSet<string> KeyReasons = new(StringComparer.Ordinal)
    {
        "invalid_key", "device_limit", "expired", "suspended", "revoked", "not_active"
    };

    private readonly HttpClient _http;
    private readonly PlatformOptions _options;
    private readonly PlatformJwksCache _jwks;
    private readonly IClock _clock;
    private readonly ILogger<PlatformLicenseClient> _logger;

    public PlatformLicenseClient(
        HttpClient http,
        IOptionsMonitor<PlatformOptions> options,
        PlatformJwksCache jwks,
        IClock clock,
        ILogger<PlatformLicenseClient> logger)
    {
        _http = http;
        _options = options.CurrentValue;
        _jwks = jwks;
        _clock = clock;
        _logger = logger;
        if (_options.IsConfigured)
        {
            _http.BaseAddress = new Uri(_options.BaseUrl!.TrimEnd('/') + "/");
            _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 2, 60));
        }
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<LicensePlatformOutcome> ValidateAsync(LicenseValidateCall call, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return LicensePlatformOutcome.NotAvailable("not_configured");
        }

        PlatformValidateResponse? body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/licenses/validate")
            {
                Content = JsonContent.Create(new PlatformValidateRequest
                {
                    LicenseKey = call.LicenseKey,
                    ServiceCode = _options.ServiceCode,
                    DeviceId = call.DeviceId,
                    DeviceName = Trim(call.DeviceName, 100),
                    Email = call.OwnerEmail
                }, options: Json)
            };
            request.Headers.Add("X-Integration-Key", _options.IntegrationKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                var retry = await RetryAfterAsync(response, cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Platform license check not judged: HTTP {Status}; retry after {Retry}", (int)response.StatusCode, retry);
                return LicensePlatformOutcome.NotAvailable(response.StatusCode is HttpStatusCode.TooManyRequests ? "rate_limited" : "service_unavailable", retry);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogError("Platform license check rejected the request: HTTP {Status}. Check Platform:BaseUrl / IntegrationKey.", (int)response.StatusCode);
                return LicensePlatformOutcome.NotAvailable("http_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
            }

            body = await response.Content.ReadFromJsonAsync<PlatformValidateResponse>(Json, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Platform license check timed out");
            return LicensePlatformOutcome.NotAvailable("timeout");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            _logger.LogWarning("Platform license check failed: {Error}", ex.GetType().Name);
            return LicensePlatformOutcome.NotAvailable("network");
        }

        if (body is null)
        {
            return LicensePlatformOutcome.NotAvailable("bad_response");
        }

        var reason = body.Reason ?? (body.IsValid ? "ok" : "unknown");
        if (!body.IsValid)
        {
            if (!KeyReasons.Contains(reason))
            {
                // invalid_integration_key, wrong_service, device_required, invalid_request: NeedleBase's own setup.
                _logger.LogError("Platform refused NeedleBase's license check ({Reason}). Check Platform:IntegrationKey and Platform:ServiceCode.", reason);
                return LicensePlatformOutcome.NotAvailable(reason);
            }

            return LicensePlatformOutcome.Judged(
                new LicenseCheck(false, reason, body.LicenseId, body.PlanName, ExpiresAtUtc: Utc(body.ExpiresAt)),
                body.EmailMatches);
        }

        var now = _clock.UtcNow;
        var expect = new LicenseTokenVerifier.Expectation(_options.ServiceCode, call.DeviceId,
            string.IsNullOrWhiteSpace(_options.TokenIssuer) ? null : _options.TokenIssuer.Trim(), body.LicenseId);
        try
        {
            var keys = await _jwks.GetAsync(_http, keyMissing: false, now, cancellationToken).ConfigureAwait(false);
            var check = LicenseTokenVerifier.Verify(body.Token, keys, expect, now, out var error, out _);
            if (check is null && error == "token_unknown_kid")
            {
                keys = await _jwks.GetAsync(_http, keyMissing: true, now, cancellationToken).ConfigureAwait(false);
                check = LicenseTokenVerifier.Verify(body.Token, keys, expect, now, out error, out _);
            }

            if (check is null)
            {
                _logger.LogError("Platform said the license is valid but its token didn't verify ({Error}); not trusted.", error);
                return LicensePlatformOutcome.NotAvailable(error ?? "token_invalid");
            }

            return LicensePlatformOutcome.Judged(check, body.EmailMatches);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LicensePlatformOutcome.NotAvailable("timeout");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            _logger.LogWarning("Couldn't load Platform's license keys: {Error}", ex.GetType().Name);
            return LicensePlatformOutcome.NotAvailable("jwks_unavailable");
        }
    }

    private static async Task<TimeSpan?> RetryAfterAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        try
        {
            var error = await response.Content.ReadFromJsonAsync<PlatformErrorResponse>(Json, cancellationToken).ConfigureAwait(false);
            return error is { RetryAfterSeconds: > 0 } ? TimeSpan.FromSeconds(Math.Min(error.RetryAfterSeconds, 86_400)) : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => new DateTimeOffset(v),
        { Kind: DateTimeKind.Local } v => new DateTimeOffset(v.ToUniversalTime()),
        { } v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc))
    };

    private static string? Trim(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
}
