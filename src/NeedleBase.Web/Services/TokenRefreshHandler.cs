using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Web.Services;

public sealed class TokenRefreshHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TokenRefreshHandler> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();
    private static readonly ConcurrentDictionary<string, CachedTokens> LatestTokensByUser = new();
    private const int MaxRetries = 3;

    private sealed record CachedTokens(string AccessToken, string RefreshToken);

    /// <summary>Drop cached tokens after logout so a later 401 cannot revive a prior session.</summary>
    public static void ClearCachedTokens(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        LatestTokensByUser.TryRemove(userId, out _);
    }

    public TokenRefreshHandler(
        IHttpContextAccessor httpContextAccessor,
        IHttpClientFactory httpClientFactory,
        ILogger<TokenRefreshHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx?.User?.Identity?.IsAuthenticated != true)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // ctx.User was set by the cookie middleware (and replaced after a refresh in this request). Pages fetch in
        // parallel, so never re-run AuthenticateAsync here: HttpContext/auth handlers aren't thread-safe, and re-reading
        // the request cookie would put the pre-refresh token back.
        var accessToken = ctx.User.FindFirst("AccessToken")?.Value;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // Buffer content once so retries / post-refresh resend can clone the request.
        await BufferRequestContentAsync(request, cancellationToken);
        ApplyAccessTokenHeaders(request, accessToken);

        HttpResponseMessage response;
        try
        {
            response = await SendWithTransientRetry(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "API request failed — backend unreachable: {Url}", request.RequestUri);
            return CreateApiUnavailableResponse(request);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "API request timed out: {Url}", request.RequestUri);
            return CreateApiTimeoutResponse(request);
        }

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        _logger.LogInformation("401 from API; attempting token refresh for {Url}", request.RequestUri);

        var outcome = await TryRefreshTokenAsync(ctx, accessToken, cancellationToken);
        if (outcome == RefreshOutcome.Transient)
        {
            // Keep cookie/session — same honesty rule as mobile (do not force login on outage).
            response.Dispose();
            return CreateApiUnavailableResponse(request);
        }

        if (outcome != RefreshOutcome.Success)
        {
            return response; // signed out inside the refresh lock (one writer to the response cookies)
        }

        var newAccessToken = ctx.User.FindFirst("AccessToken")?.Value;
        if (string.IsNullOrWhiteSpace(newAccessToken))
        {
            return response;
        }

        response.Dispose();
        using var retryRequest = await CloneHttpRequestMessageAsync(request, cancellationToken);
        ApplyAccessTokenHeaders(retryRequest, newAccessToken);
        try
        {
            return await SendWithTransientRetry(retryRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Retried API request failed — backend unreachable: {Url}", retryRequest.RequestUri);
            return CreateApiUnavailableResponse(retryRequest);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Retried API request timed out: {Url}", retryRequest.RequestUri);
            return CreateApiTimeoutResponse(retryRequest);
        }
    }

    private enum RefreshOutcome
    {
        Success,
        InvalidSession,
        Transient
    }

    private async Task<RefreshOutcome> TryRefreshTokenAsync(
        HttpContext ctx,
        string currentAccessToken,
        CancellationToken cancellationToken)
    {
        var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? ctx.User.FindFirst(ClaimTypes.Email)?.Value
                     ?? ctx.User.Identity?.Name
                     ?? "unknown";

        var userLock = UserLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        if (!await userLock.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken))
        {
            return RefreshOutcome.Transient;
        }

        try
        {
            // Another request on this process may have already rotated; avoid reuse-revoke.
            if (LatestTokensByUser.TryGetValue(userId, out var cached) &&
                !string.IsNullOrWhiteSpace(cached.AccessToken) &&
                cached.AccessToken != currentAccessToken)
            {
                await UpdateAuthenticationCookieAsync(ctx, cached.AccessToken, cached.RefreshToken);
                return RefreshOutcome.Success;
            }

            var latestAccessToken = ctx.User.FindFirst("AccessToken")?.Value;
            if (!string.IsNullOrWhiteSpace(latestAccessToken) && latestAccessToken != currentAccessToken)
            {
                return RefreshOutcome.Success;
            }

            var refreshToken = ctx.User.FindFirst("RefreshToken")?.Value;
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                await SignOutAsync(ctx);
                return RefreshOutcome.InvalidSession;
            }

            using var authClient = _httpClientFactory.CreateClient("AuthApi");
            var refreshRequest = new RefreshTokenRequest(currentAccessToken, refreshToken);

            HttpResponseMessage refreshResponse;
            try
            {
                refreshResponse = await authClient.PostAsJsonAsync(
                    "api/auth/refresh-token",
                    refreshRequest,
                    cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Refresh request failed — backend unreachable for user {UserId}", userId);
                return RefreshOutcome.Transient;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Refresh request timed out for user {UserId}", userId);
                return RefreshOutcome.Transient;
            }

            if ((int)refreshResponse.StatusCode >= 500 ||
                refreshResponse.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return RefreshOutcome.Transient;
            }

            if (!refreshResponse.IsSuccessStatusCode)
            {
                await SignOutAsync(ctx);
                return RefreshOutcome.InvalidSession;
            }

            var tokenResponse = await refreshResponse.Content.ReadFromJsonAsync<RefreshPayload>(cancellationToken);
            if (tokenResponse is null ||
                string.IsNullOrWhiteSpace(tokenResponse.AccessToken) ||
                string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
            {
                return RefreshOutcome.Transient;
            }

            LatestTokensByUser[userId] = new CachedTokens(tokenResponse.AccessToken, tokenResponse.RefreshToken);
            await UpdateAuthenticationCookieAsync(ctx, tokenResponse.AccessToken, tokenResponse.RefreshToken);
            return RefreshOutcome.Success;
        }
        finally
        {
            userLock.Release();
        }
    }

    private async Task UpdateAuthenticationCookieAsync(HttpContext ctx, string accessToken, string refreshToken)
    {
        var authenticateResult = await ctx.AuthenticateAsync(AuthCookieSignIn.CookieScheme);
        if (!authenticateResult.Succeeded || authenticateResult.Principal is null)
        {
            return;
        }

        if (authenticateResult.Principal.Identity is not ClaimsIdentity original)
        {
            return;
        }

        // Work on a copy: the cached cookie identity may be read by a sibling request on another thread.
        var identity = original.Clone();

        ReplaceClaim(identity, "AccessToken", accessToken);
        ReplaceClaim(identity, "RefreshToken", refreshToken);
        RefreshRoleClaimsFromAccessToken(identity, accessToken);

        var properties = authenticateResult.Properties ?? new AuthenticationProperties();
        await ctx.SignInAsync(AuthCookieSignIn.CookieScheme, new ClaimsPrincipal(identity), properties);
        ctx.User = new ClaimsPrincipal(identity);
    }

    private void RefreshRoleClaimsFromAccessToken(ClaimsIdentity identity, string accessToken)
    {
        try
        {
            var token = accessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? accessToken[7..]
                : accessToken;

            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                ClearRoleClaims(identity);
                return;
            }

            var jwt = handler.ReadJwtToken(token);
            var roles = jwt.Claims
                .Where(c => c.Type is ClaimTypes.Role or "role")
                .Select(c => c.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            ClearRoleClaims(identity);
            foreach (var role in roles)
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh role claims from access token");
            ClearRoleClaims(identity);
        }
    }

    private static void ReplaceClaim(ClaimsIdentity identity, string type, string value)
    {
        var existing = identity.FindFirst(type);
        if (existing is not null)
        {
            identity.RemoveClaim(existing);
        }

        identity.AddClaim(new Claim(type, value));
    }

    private static void ClearRoleClaims(ClaimsIdentity identity)
    {
        foreach (var existing in identity.FindAll(ClaimTypes.Role).ToList())
        {
            identity.RemoveClaim(existing);
        }

        foreach (var existing in identity.FindAll("role").ToList())
        {
            identity.RemoveClaim(existing);
        }
    }

    private static void ApplyAccessTokenHeaders(HttpRequestMessage request, string accessToken)
    {
        var bearer = accessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? accessToken
            : $"Bearer {accessToken}";

        request.Headers.Remove("X-Auth-Token");
        request.Headers.TryAddWithoutValidation("X-Auth-Token", bearer);
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(bearer);
    }

    private async Task<HttpResponseMessage> SendWithTransientRetry(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var attemptRequest = await CloneHttpRequestMessageAsync(request, cancellationToken);
            try
            {
                return await base.SendAsync(attemptRequest, cancellationToken);
            }
            // Retry only when the request never reached the Api (connection refused). A timeout may mean the Api
            // already applied it: replaying a PATCH/POST would double-apply or report a false STALE_UPDATE, and
            // retrying GET timeouts tripled the wait (3 × 30 s) before the offline state showed.
            catch (HttpRequestException ex) when (attempt < MaxRetries && IsConnectRefused(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }
    }

    private static async Task BufferRequestContentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null || request.Content is ByteArrayContent)
        {
            return;
        }

        var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var buffered = new ByteArrayContent(bytes);
        foreach (var header in request.Content.Headers)
        {
            buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        request.Content = buffered;
    }

    private static async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        foreach (var option in request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        return clone;
    }

    internal static bool IsConnectRefused(HttpRequestException ex) =>
        ex.HttpRequestError == HttpRequestError.ConnectionError ||
        ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused };

    private async Task SignOutAsync(HttpContext ctx)
    {
        try
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? ctx.User.FindFirst(ClaimTypes.Email)?.Value
                         ?? ctx.User.Identity?.Name;
            ClearCachedTokens(userId);
            await ctx.SignOutAsync(AuthCookieSignIn.CookieScheme);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error signing out after refresh failure");
        }
    }

    private static HttpResponseMessage CreateApiUnavailableResponse(HttpRequestMessage request) =>
        new(HttpStatusCode.ServiceUnavailable)
        {
            RequestMessage = request,
            ReasonPhrase = "NeedleBase API is unavailable"
        };

    private static HttpResponseMessage CreateApiTimeoutResponse(HttpRequestMessage request) =>
        new(HttpStatusCode.GatewayTimeout)
        {
            RequestMessage = request,
            ReasonPhrase = "NeedleBase API request timed out"
        };

    private sealed class RefreshPayload
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }
}
