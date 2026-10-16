using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Mobile.Configuration;

namespace NeedleBase.Mobile.Services;

public sealed class AuthService : IAuthService
{
    public const string AccessTokenKey = "access_token";
    public const string RefreshTokenKey = "refresh_token";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecureStorageService _secureStorage;
    private readonly ApiSettings _apiSettings;

    public AuthService(
        IHttpClientFactory httpClientFactory,
        ISecureStorageService secureStorage,
        IOptions<ApiSettings> apiSettings)
    {
        _httpClientFactory = httpClientFactory;
        _secureStorage = secureStorage;
        _apiSettings = apiSettings.Value;
    }

    public async Task<AuthUserDto?> RegisterBusinessAsync(
        RegisterBusinessRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = CreateAuthClient();
        using var response = await client.PostAsJsonAsync(
            "api/auth/register-business",
            request,
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<AuthResponsePayload>(JsonOptions, cancellationToken);
        if (payload?.AccessToken is null || payload.RefreshToken is null || payload.User is null)
        {
            return null;
        }

        await StoreTokensAsync(new TokenResponse(payload.AccessToken, payload.RefreshToken));
        return payload.User;
    }

    public async Task<AuthUserDto?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var client = CreateAuthClient();
        using var response = await client.PostAsJsonAsync("api/auth/login", request, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<AuthResponsePayload>(JsonOptions, cancellationToken);
        if (payload?.AccessToken is null || payload.RefreshToken is null || payload.User is null)
        {
            return null;
        }

        await StoreTokensAsync(new TokenResponse(payload.AccessToken, payload.RefreshToken));
        return payload.User;
    }

    public async Task<TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            var refreshToken = await _secureStorage.GetAsync(RefreshTokenKey);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return TokenRefreshResult.InvalidSession("No refresh token.");
            }

            var client = CreateAuthClient();
            var body = new RefreshTokenRequest(string.Empty, refreshToken);
            using var response = await client.PostAsJsonAsync("api/auth/refresh-token", body, JsonOptions, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                await ClearTokensAsync();
                return TokenRefreshResult.InvalidSession("Session expired.");
            }

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return TokenRefreshResult.Transient($"Server error {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                return TokenRefreshResult.Transient($"Refresh failed {(int)response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync<AuthResponsePayload>(JsonOptions, cancellationToken);
            if (payload?.AccessToken is null || payload.RefreshToken is null)
            {
                return TokenRefreshResult.Transient("Invalid refresh payload.");
            }

            var tokens = new TokenResponse(payload.AccessToken, payload.RefreshToken);
            await StoreTokensAsync(tokens);
            return TokenRefreshResult.Ok(tokens);
        }
        catch (HttpRequestException ex)
        {
            return TokenRefreshResult.Transient(ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return TokenRefreshResult.Transient(ex.Message);
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    public async Task<bool> HasStoredTokensAsync()
    {
        var access = await _secureStorage.GetAsync(AccessTokenKey);
        var refresh = await _secureStorage.GetAsync(RefreshTokenKey);
        return !string.IsNullOrWhiteSpace(access) && !string.IsNullOrWhiteSpace(refresh);
    }

    public Task ClearTokensAsync()
    {
        _secureStorage.Remove(AccessTokenKey);
        _secureStorage.Remove(RefreshTokenKey);
        return Task.CompletedTask;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var refreshToken = await _secureStorage.GetAsync(RefreshTokenKey);
        try
        {
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var client = CreateAuthClient();
                using var response = await client.PostAsJsonAsync(
                    "api/auth/logout",
                    new LogoutRequest(refreshToken),
                    JsonOptions,
                    cancellationToken);
                _ = response;
            }
        }
        catch (HttpRequestException)
        {
            // Still clear local tokens; server revoke is best-effort when offline.
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await ClearTokensAsync();
        }
    }

    public Task<string?> GetAccessTokenAsync() =>
        _secureStorage.GetAsync(AccessTokenKey);

    public async Task StoreTokensAsync(TokenResponse tokens)
    {
        await _secureStorage.SetAsync(AccessTokenKey, tokens.AccessToken);
        await _secureStorage.SetAsync(RefreshTokenKey, tokens.RefreshToken);
    }

    private HttpClient CreateAuthClient()
    {
        var client = _httpClientFactory.CreateClient("AuthClient");
        var baseUrl = _apiSettings.BaseUrl;
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        client.BaseAddress = new Uri(baseUrl);
        return client;
    }

    private sealed record AuthResponsePayload(
        string? AccessToken,
        string? RefreshToken,
        AuthUserDto? User);
}
