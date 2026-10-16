using System.Net.Http.Json;
using System.Text.Json;
using NeedleBase.Application.Contracts.Auth;

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

    public async Task<AuthSessionResult?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/auth/login", request, JsonOptions, cancellationToken);
        return await ReadSessionAsync(response, cancellationToken);
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
        return await ReadSessionAsync(response, cancellationToken);
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

    private async Task<AuthSessionResult?> ReadSessionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
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
