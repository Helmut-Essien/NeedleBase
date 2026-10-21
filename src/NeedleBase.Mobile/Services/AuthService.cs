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

    /// <summary>Who the local SQLite data belongs to ("userId|businessId"). Not cleared when a session expires.</summary>
    public const string LocalDataOwnerKey = "local_data_owner";

    /// <summary>
    /// Set when refresh says the account is deactivated / no longer a member; cleared only after the local wipe.
    /// Survives a crash or kill, so the next start finishes the wipe.
    /// </summary>
    public const string AccessRevokedKey = "access_revoked";

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

        if (!await AcceptOwnerChangeAsync(payload.User, payload.RefreshToken, cancellationToken))
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

        if (!await AcceptOwnerChangeAsync(payload.User, payload.RefreshToken, cancellationToken))
        {
            return null;
        }

        await StoreTokensAsync(new TokenResponse(payload.AccessToken, payload.RefreshToken));
        await StoreSessionAsync(payload.User);
        return payload.User;
    }

    public Task<TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(staleAccessToken: null, onlyIfStale: false, cancellationToken);

    public Task<TokenRefreshResult> RefreshIfStaleAsync(string? staleAccessToken, CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(staleAccessToken, onlyIfStale: true, cancellationToken);

    private async Task<TokenRefreshResult> RefreshCoreAsync(string? staleAccessToken, bool onlyIfStale, CancellationToken cancellationToken)
    {
        // One lock for every refresh in the app (startup, API handler, sync): refresh tokens rotate.
        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (onlyIfStale)
            {
                var current = await _secureStorage.GetAsync(AccessTokenKey);
                if (!string.IsNullOrWhiteSpace(current) && current != staleAccessToken)
                {
                    return TokenRefreshResult.Ok(
                        new TokenResponse(current, await _secureStorage.GetAsync(RefreshTokenKey) ?? string.Empty),
                        "Already refreshed");
                }
            }

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
                var code = ReadProblemCode(await response.Content.ReadAsStringAsync(cancellationToken));
                if (AccessRevokedCodes.IsRevoked(code))
                {
                    await _secureStorage.SetAsync(AccessRevokedKey, code!);
                }

                await ClearTokensAsync();
                return TokenRefreshResult.InvalidSession("Session expired.", code);
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

    public async Task<string?> GetAccessRevokedAsync()
    {
        var code = await _secureStorage.GetAsync(AccessRevokedKey);
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }

    public async Task<string?> GetLocalDataBusinessIdAsync()
    {
        var owner = await _secureStorage.GetAsync(LocalDataOwnerKey);
        var bar = owner?.IndexOf('|') ?? -1;
        return bar >= 0 ? owner![(bar + 1)..] : null;
    }

    public async Task<bool> WipeRevokedAccessAsync(CancellationToken cancellationToken = default)
    {
        if (!await _localDatabase.WipeAsync(cancellationToken))
        {
            return false; // flag kept: the next start tries again
        }

        await ClearTokensAsync();
        _secureStorage.Remove(LocalDataOwnerKey);
        _secureStorage.Remove(AccessRevokedKey);
        return true;
    }

    private static string? ReadProblemCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
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
        _secureStorage.Remove(LocalDataOwnerKey);
        return true;
    }

    public Task<string?> GetAccessTokenAsync() =>
        _secureStorage.GetAsync(AccessTokenKey);

    public async Task StoreTokensAsync(TokenResponse tokens)
    {
        await _secureStorage.SetAsync(AccessTokenKey, tokens.AccessToken);
        await _secureStorage.SetAsync(RefreshTokenKey, tokens.RefreshToken);
    }

    public Func<int, Task<bool>>? OtherAccountDataPrompt { get; set; }

    public const string OtherAccountCancelledMessage =
        "Sign-in cancelled. The unsynced changes from the other account are still on this device.";

    /// <summary>
    /// A different account/business is about to replace this device's data. If that data includes unsynced
    /// changes, the user must confirm; otherwise the new session is dropped (its refresh token revoked) and the
    /// old data stays untouched.
    /// </summary>
    private async Task<bool> AcceptOwnerChangeAsync(AuthUserDto user, string refreshToken, CancellationToken cancellationToken)
    {
        var previous = await _secureStorage.GetAsync(LocalDataOwnerKey);
        if (string.IsNullOrEmpty(previous) || previous == user.UserId + "|" + user.BusinessId)
        {
            return true;
        }

        var pending = await _localDatabase.CountUnsyncedAsync(cancellationToken);
        if (pending == 0 || (OtherAccountDataPrompt is { } prompt && await prompt(pending)))
        {
            return true;
        }

        LastErrorMessage = OtherAccountCancelledMessage;
        try
        {
            using var response = await CreateAuthClient().PostAsJsonAsync(
                "api/auth/logout", new LogoutRequest(refreshToken), JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Best effort: the unused token simply expires.
        }

        return false;
    }

    public async Task StoreSessionAsync(AuthUserDto user)
    {
        // Local data belongs to one account + business. Signing in again as the same one (e.g. after the
        // session expired) keeps unsynced work; a different account or business starts from a wiped database.
        var owner = user.UserId + "|" + user.BusinessId;
        var previous = await _secureStorage.GetAsync(LocalDataOwnerKey);
        if (!string.IsNullOrEmpty(previous) && previous != owner && !await _localDatabase.WipeAsync())
        {
            await ClearTokensAsync();
            throw new InvalidOperationException("Local data from another account couldn't be cleared.");
        }

        await _secureStorage.SetAsync(LocalDataOwnerKey, owner);
        _secureStorage.Remove(AccessRevokedKey);
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

