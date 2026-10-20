using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Data.Services;

namespace NeedleBase.Mobile.Services;

public sealed class AuthService : IAuthService
{
    public const string AccessTokenKey = "access_token";
    public const string RefreshTokenKey = "refresh_token";
    public const string BusinessIdKey = "business_id";
    public const string BusinessNameKey = "business_name";
    public const string UserIdKey = "user_id";
    public const string EmailKey = "email";
    public const string FirstNameKey = "first_name";
    public const string SurnameKey = "surname";
    public const string RoleKey = "role";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecureStorageService _secureStorage;
    private readonly ILocalDatabaseService _localDatabase;
    private readonly ApiSettings _apiSettings;

    public AuthService(
        IHttpClientFactory httpClientFactory,
        ISecureStorageService secureStorage,
        ILocalDatabaseService localDatabase,
        IOptions<ApiSettings> apiSettings)
    {
        _httpClientFactory = httpClientFactory;
        _secureStorage = secureStorage;
        _localDatabase = localDatabase;
        _apiSettings = apiSettings.Value;
    }

    public string? LastErrorMessage { get; private set; }

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

        LastErrorMessage = null;
        if (!response.IsSuccessStatusCode)
        {
            LastErrorMessage = AuthErrorMessages.ForRegister(await ApiProblem.ReadAsync(response, cancellationToken));
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<AuthResponsePayload>(JsonOptions, cancellationToken);
        if (payload?.AccessToken is null || payload.RefreshToken is null || payload.User is null)
        {
            return null;
        }

        await StoreTokensAsync(new TokenResponse(payload.AccessToken, payload.RefreshToken));
        await StoreSessionAsync(payload.User);
        return payload.User;
    }

    public async Task<AuthUserDto?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var client = CreateAuthClient();
        using var response = await client.PostAsJsonAsync("api/auth/login", request, JsonOptions, cancellationToken);
        LastErrorMessage = null;
        if (!response.IsSuccessStatusCode)
        {
            LastErrorMessage = AuthErrorMessages.ForLogin(await ApiProblem.ReadAsync(response, cancellationToken));
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<AuthResponsePayload>(JsonOptions, cancellationToken);
        if (payload?.AccessToken is null || payload.RefreshToken is null || payload.User is null)
        {
            return null;
        }

        await StoreTokensAsync(new TokenResponse(payload.AccessToken, payload.RefreshToken));
        await StoreSessionAsync(payload.User);
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
            if (payload.User is not null)
            {
                await StoreSessionAsync(payload.User);
            }

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
        _secureStorage.Remove(BusinessIdKey);
        _secureStorage.Remove(BusinessNameKey);
        _secureStorage.Remove(UserIdKey);
        _secureStorage.Remove(EmailKey);
        _secureStorage.Remove(FirstNameKey);
        _secureStorage.Remove(SurnameKey);
        _secureStorage.Remove(RoleKey);
        return Task.CompletedTask;
    }

    public async Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
    {
        // Wipe local SQLite before clearing tokens — abort logout if wipe fails
        // so the next user cannot read prior tenant PII (mobile hard rule).
        if (!await _localDatabase.WipeAsync(cancellationToken))
        {
            return false;
        }

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
            // Server revoke is best-effort when offline; local wipe already succeeded.
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        await ClearTokensAsync();
        return true;
    }

    public Task<string?> GetAccessTokenAsync() =>
        _secureStorage.GetAsync(AccessTokenKey);

    public async Task StoreTokensAsync(TokenResponse tokens)
    {
        await _secureStorage.SetAsync(AccessTokenKey, tokens.AccessToken);
        await _secureStorage.SetAsync(RefreshTokenKey, tokens.RefreshToken);
    }

    public async Task StoreSessionAsync(AuthUserDto user)
    {
        await _secureStorage.SetAsync(BusinessIdKey, user.BusinessId);
        await _secureStorage.SetAsync(BusinessNameKey, user.BusinessName);
        await _secureStorage.SetAsync(UserIdKey, user.UserId);
        await _secureStorage.SetAsync(EmailKey, user.Email);
        await _secureStorage.SetAsync(FirstNameKey, user.FirstName);
        await _secureStorage.SetAsync(SurnameKey, user.Surname);
        await _secureStorage.SetAsync(RoleKey, user.Role);
    }

    public Task<string?> GetBusinessIdAsync() =>
        _secureStorage.GetAsync(BusinessIdKey);

    public async Task<AuthUserDto?> GetCachedUserAsync()
    {
        var userId = await _secureStorage.GetAsync(UserIdKey);
        var businessId = await _secureStorage.GetAsync(BusinessIdKey);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(businessId))
        {
            return null;
        }

        return new AuthUserDto(
            userId,
            await _secureStorage.GetAsync(EmailKey) ?? string.Empty,
            await _secureStorage.GetAsync(FirstNameKey) ?? string.Empty,
            await _secureStorage.GetAsync(SurnameKey) ?? string.Empty,
            businessId,
            await _secureStorage.GetAsync(BusinessNameKey) ?? string.Empty,
            await _secureStorage.GetAsync(RoleKey) ?? string.Empty);
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

