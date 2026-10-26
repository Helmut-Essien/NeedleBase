using System.Net.Http.Json;
using System.Text.Json;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Errors;

namespace NeedleBase.Web.Services;

public sealed class ApiAuthService : IApiAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiAuthService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ApiAuthService(IHttpClientFactory httpClientFactory, ILogger<ApiAuthService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("AuthApi");
        _logger = logger;
    }

    public string? LastErrorMessage { get; private set; }

    public async Task<AuthSessionResult?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/auth/login", request, JsonOptions, cancellationToken);
        return await ReadSessionAsync(response, AuthErrorMessages.ForLogin, cancellationToken);
    }

    public async Task<AuthSessionResult?> RegisterBusinessAsync(
        RegisterBusinessRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/auth/register-business",
            request,
            JsonOptions,
            cancellationToken);
        return await ReadSessionAsync(response, AuthErrorMessages.ForRegister, cancellationToken);
    }

    public async Task<TokenResponse?> RefreshAsync(
        string accessToken,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var body = new RefreshTokenRequest(accessToken, refreshToken);
        using var response = await _httpClient.PostAsJsonAsync(
            "api/auth/refresh-token",
            body,
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Refresh token request failed with status {Status}", response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<AuthApiPayload>(JsonOptions, cancellationToken);
        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.AccessToken) ||
            string.IsNullOrWhiteSpace(payload.RefreshToken))
        {
            return null;
        }

        return new TokenResponse(payload.AccessToken, payload.RefreshToken);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/auth/logout",
                new LogoutRequest(refreshToken),
                JsonOptions,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Logout API returned {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logout API call failed; clearing local session anyway.");
        }
    }

    public async Task<NeedleBase.Application.Contracts.Staff.InvitePreviewDto?> PreviewInviteAsync(string code, CancellationToken cancellationToken = default)
    {
        LastErrorMessage = null;
        using var response = await _httpClient.PostAsJsonAsync("api/auth/invite-preview", new InviteCodeRequest { Code = code }, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LastErrorMessage = InviteError(await ApiProblem.ReadAsync(response, cancellationToken));
            return null;
        }

        return await response.Content.ReadFromJsonAsync<NeedleBase.Application.Contracts.Staff.InvitePreviewDto>(JsonOptions, cancellationToken);
    }

    public async Task<AuthSessionResult?> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/auth/accept-invite", request, JsonOptions, cancellationToken);
        return await ReadSessionAsync(response, InviteError, cancellationToken);
    }

    private static string InviteError(ApiProblem problem) => StaffErrorMessages.ForProblem(problem);

    private static (bool Enabled, DateTimeOffset Until, bool Known) _emailEnabled;
    private static int _emailRefreshing;

    /// <summary>
    /// Whether the Api can send email (shows "Forgot password?"). Cached 5 min. Once a value is known, an expired cache
    /// answers at once and refreshes in the background (one refresh at a time), so a slow or down Api never adds
    /// seconds to the sign-in page and a brief blip doesn't hide the link.
    /// </summary>
    public async Task<bool> EmailEnabledAsync(CancellationToken cancellationToken = default)
    {
        var cached = _emailEnabled;
        if (cached.Until > DateTimeOffset.UtcNow)
        {
            return cached.Enabled;
        }

        if (cached.Known)
        {
            if (Interlocked.CompareExchange(ref _emailRefreshing, 1, 0) == 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RefreshEmailEnabledAsync(CancellationToken.None);
                    }
                    finally
                    {
                        Volatile.Write(ref _emailRefreshing, 0);
                    }
                }, CancellationToken.None);
            }

            return cached.Enabled;
        }

        return await RefreshEmailEnabledAsync(cancellationToken);
    }

    private async Task<bool> RefreshEmailEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2)); // first ever call only; later ones never wait
            var dto = await _httpClient.GetFromJsonAsync<AuthOptionsDto>("api/auth/options", JsonOptions, timeout.Token);
            var enabled = dto?.EmailEnabled == true;
            _emailEnabled = (enabled, DateTimeOffset.UtcNow.AddMinutes(5), true);
            return enabled;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            _logger.LogInformation("Couldn't read Api auth options ({Type}); keeping the last answer for a minute", ex.GetType().Name);
            var last = _emailEnabled;
            _emailEnabled = (last.Known && last.Enabled, DateTimeOffset.UtcNow.AddMinutes(1), last.Known);
            return _emailEnabled.Enabled;
        }
    }

    /// <summary>Tests: make the cached answer stale (kept as the last known value).</summary>
    internal static void ExpireEmailEnabledCache() => _emailEnabled = _emailEnabled with { Until = DateTimeOffset.MinValue };

    /// <summary>Tests: forget the cached answer.</summary>
    internal static void ResetEmailEnabledCache()
    {
        _emailEnabled = default;
        Volatile.Write(ref _emailRefreshing, 0);
    }

    public async Task<bool> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        LastErrorMessage = null;
        using var response = await _httpClient.PostAsJsonAsync("api/auth/forgot-password", new ForgotPasswordRequest { Email = email }, JsonOptions, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        LastErrorMessage = StaffErrorMessages.ForProblem(await ApiProblem.ReadAsync(response, cancellationToken));
        return false;
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        LastErrorMessage = null;
        using var response = await _httpClient.PostAsJsonAsync("api/auth/reset-password", request, JsonOptions, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        LastErrorMessage = StaffErrorMessages.ForProblem(await ApiProblem.ReadAsync(response, cancellationToken));
        return false;
    }

    private async Task<AuthSessionResult?> ReadSessionAsync(
        HttpResponseMessage response,
        Func<ApiProblem, string> describe,
        CancellationToken cancellationToken)
    {
        LastErrorMessage = null;
        if (!response.IsSuccessStatusCode)
        {
            var problem = await ApiProblem.ReadAsync(response, cancellationToken);
            _logger.LogInformation("Auth API returned {Status} {Code}", problem.Status, problem.Code);
            LastErrorMessage = describe(problem);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<AuthApiPayload>(JsonOptions, cancellationToken);
        if (payload?.User is null ||
            string.IsNullOrWhiteSpace(payload.AccessToken) ||
            string.IsNullOrWhiteSpace(payload.RefreshToken))
        {
            _logger.LogWarning("Auth API returned success without tokens or user payload.");
            return null;
        }

        return new AuthSessionResult(
            new TokenResponse(payload.AccessToken, payload.RefreshToken),
            payload.User);
    }

    private sealed class AuthApiPayload
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public AuthUserDto? User { get; set; }
    }
}
