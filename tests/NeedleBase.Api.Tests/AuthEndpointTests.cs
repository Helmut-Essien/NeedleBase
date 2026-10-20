using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Api.Tests;

public class AuthEndpointTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _client;

    public AuthEndpointTests(NeedleBaseApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsProblemDetailsWithCode()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest($"nobody{Guid.NewGuid():N}@ama-stitches.test", "WrongPass1!"),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(401, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.Equal("INVALID_CREDENTIALS", root.GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task RegisterLoginMe_HappyPath_IncludesBusinessClaims()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"owner{suffix}@ama-stitches.test";
        var register = new RegisterBusinessRequest(
            email,
            "Passw0rd!",
            "Ama",
            "Owusu",
            $"Ama Stitches {suffix}",
            "+233200000000");

        using var registerResponse = await _client.PostAsJsonAsync(
            "/api/auth/register-business",
            register,
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var registered = await registerResponse.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        Assert.NotNull(registered);
        Assert.False(string.IsNullOrWhiteSpace(registered.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(registered.RefreshToken));
        Assert.Equal("Owner", registered.User?.Role);
        Assert.False(string.IsNullOrWhiteSpace(registered.User?.BusinessId));

        using var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, "Passw0rd!"),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loggedIn = await loginResponse.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        Assert.NotNull(loggedIn?.AccessToken);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loggedIn.AccessToken);
        using var meResponse = await _client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var me = await meResponse.Content.ReadFromJsonAsync<MePayload>(JsonOptions);
        Assert.NotNull(me);
        Assert.Equal(registered.User!.BusinessId, me.BusinessId);
        Assert.Equal("Owner", me.Role);

        // X-Auth-Token mapping
        using var xAuthRequest = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        xAuthRequest.Headers.TryAddWithoutValidation("X-Auth-Token", $"Bearer {loggedIn.AccessToken}");
        using var xAuthResponse = await _client.SendAsync(xAuthRequest);
        Assert.Equal(HttpStatusCode.OK, xAuthResponse.StatusCode);
    }

    [Fact]
    public async Task Login_IsCaseInsensitiveOnEmail()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"Case{suffix}@Ama-Stitches.test";
        var register = new RegisterBusinessRequest(
            email,
            "Passw0rd!",
            "Ama",
            "Owusu",
            $"Case Biz {suffix}",
            null);

        using var registerResponse = await _client.PostAsJsonAsync(
            "/api/auth/register-business",
            register,
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        using var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email.ToLowerInvariant(), "Passw0rd!"),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var register = new RegisterBusinessRequest(
            $"logout{suffix}@ama-stitches.test",
            "Passw0rd!",
            "Yaw",
            "Mensah",
            $"Logout Biz {suffix}",
            null);

        using var registerResponse = await _client.PostAsJsonAsync(
            "/api/auth/register-business",
            register,
            JsonOptions);
        var registered = await registerResponse.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        Assert.NotNull(registered?.RefreshToken);

        using var logoutResponse = await _client.PostAsJsonAsync(
            "/api/auth/logout",
            new LogoutRequest(registered.RefreshToken),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var refreshResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new RefreshTokenRequest(string.Empty, registered.RefreshToken),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_RotatesSuccessfully()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var register = new RegisterBusinessRequest(
            $"refresh{suffix}@ama-stitches.test",
            "Passw0rd!",
            "Kofi",
            "Asante",
            $"Refresh Biz {suffix}",
            null);

        using var registerResponse = await _client.PostAsJsonAsync(
            "/api/auth/register-business",
            register,
            JsonOptions);
        var registered = await registerResponse.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        Assert.NotNull(registered?.RefreshToken);

        using var refreshResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new RefreshTokenRequest(string.Empty, registered.RefreshToken),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        Assert.NotNull(refreshed?.AccessToken);
        Assert.NotEqual(registered.RefreshToken, refreshed.RefreshToken);
    }

    private sealed record AuthPayload(string AccessToken, string RefreshToken, AuthUserDto? User);
    private sealed record MePayload(string UserId, string BusinessId, string Role, string BusinessName);
}
