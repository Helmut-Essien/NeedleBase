using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Entities;
using NeedleBase.Application.Common;
using NeedleBase.Infrastructure.Identity;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

public class AccountDeletionApiTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly NeedleBaseApiFactory _factory;
    private readonly HttpClient _client;

    public AccountDeletionApiTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DeleteAccount_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/me/delete-account",
            new DeleteAccountRequest { Password = "Passw0rd!", Confirmation = DeleteAccountConfirmations.OwnerShop },
            JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_WrongPassword_IsRejected_AndSignInStillWorks()
    {
        var owner = await RegisterAsync("wp");
        using var response = await SendAsync(HttpMethod.Post, "/api/me/delete-account", owner.AccessToken,
            new DeleteAccountRequest { Password = "WrongPass1!", Confirmation = DeleteAccountConfirmations.OwnerShop });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("PASSWORD_INCORRECT", await CodeAsync(response));

        using var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(owner.Email, "Passw0rd!"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Owner_DeleteAccount_ClosesShop_RevokesTokens_AndBlocksSignIn()
    {
        var owner = await RegisterAsync("own");
        var staff = await AddMemberAsync(owner.AccessToken, BusinessRole.Tailor);
        using var customerResponse = await SendAsync(HttpMethod.Post, "/api/customers", owner.AccessToken,
            new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;

        using var ownerConfirm = await SendAsync(HttpMethod.Post, "/api/me/delete-account", owner.AccessToken,
            new DeleteAccountRequest { Password = "Passw0rd!", Confirmation = DeleteAccountConfirmations.Staff });
        Assert.Equal(HttpStatusCode.BadRequest, ownerConfirm.StatusCode);
        Assert.Equal("OWNER_CONFIRMATION_REQUIRED", await CodeAsync(ownerConfirm));

        using var deleted = await SendAsync(HttpMethod.Post, "/api/me/delete-account", owner.AccessToken,
            new DeleteAccountRequest { Password = "Passw0rd!", Confirmation = DeleteAccountConfirmations.OwnerShop });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var me = await SendAsync(HttpMethod.Get, "/api/me", owner.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        using var refresh = await _client.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new RefreshTokenRequest(string.Empty, owner.RefreshToken),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        using var staffRefresh = await _client.PostAsJsonAsync(
            "/api/auth/refresh-token",
            new RefreshTokenRequest(string.Empty, staff.RefreshToken),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, staffRefresh.StatusCode);

        using var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(owner.Email, "Passw0rd!"), JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await CodeAsync(login));

        using var staffLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(staff.Email, "Kofi1234"), JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, staffLogin.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CurrentBusinessId = null;
        var business = await db.Businesses.IgnoreQueryFilters().SingleAsync(b => b.Id == owner.BusinessId);
        Assert.True(business.IsClosed);
        Assert.Equal("Closed shop", business.Name);
        Assert.Null(business.LicenseKeyProtected);
        Assert.False(await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == customer.Id));
        var ownerUser = await db.Users.SingleAsync(u => u.Id == owner.UserId);
        Assert.False(ownerUser.IsActive);
        Assert.StartsWith("deleted.", ownerUser.Email);
        Assert.Equal("Deleted", ownerUser.FirstName);
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.ApplicationUserId == owner.UserId));
    }

    [Fact]
    public async Task Staff_DeleteAccount_RemovesOnlyThatUser_ShopStays()
    {
        var owner = await RegisterAsync("stf");
        var staff = await AddMemberAsync(owner.AccessToken, BusinessRole.Tailor);
        using var customerResponse = await SendAsync(HttpMethod.Post, "/api/customers", owner.AccessToken,
            new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;

        using var deleted = await SendAsync(HttpMethod.Post, "/api/me/delete-account", staff.AccessToken,
            new DeleteAccountRequest { Password = "Kofi1234", Confirmation = DeleteAccountConfirmations.Staff });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var staffMe = await SendAsync(HttpMethod.Get, "/api/me", staff.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, staffMe.StatusCode);

        using var staffLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(staff.Email, "Kofi1234"), JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, staffLogin.StatusCode);

        using var ownerLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(owner.Email, "Passw0rd!"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, ownerLogin.StatusCode);
        var loggedIn = await ownerLogin.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(loggedIn?.AccessToken));
        using var ownerMe = await SendAsync(HttpMethod.Get, "/api/me", loggedIn!.AccessToken!);
        Assert.Equal(HttpStatusCode.OK, ownerMe.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CurrentBusinessId = null;
        var business = await db.Businesses.SingleAsync(b => b.Id == owner.BusinessId);
        Assert.False(business.IsClosed);
        Assert.True(await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == customer.Id));
        var staffUser = await db.Users.SingleAsync(u => u.Id == staff.UserId);
        Assert.False(staffUser.IsActive);
        Assert.StartsWith("deleted.", staffUser.Email);
    }


    [Fact]
    public async Task DeleteAccount_Confirmation_AllowsExtraWhitespaceAndCase()
    {
        var owner = await RegisterAsync("ws");
        using var deleted = await SendAsync(HttpMethod.Post, "/api/me/delete-account", owner.AccessToken,
            new DeleteAccountRequest { Password = "Passw0rd!", Confirmation = "  delete   shop\t" });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Owner_DeleteAccount_FreesEmail_SoTheSameAddressCanRegisterAgain()
    {
        var owner = await RegisterAsync("em");
        using var deleted = await SendAsync(HttpMethod.Post, "/api/me/delete-account", owner.AccessToken,
            new DeleteAccountRequest { Password = "Passw0rd!", Confirmation = DeleteAccountConfirmations.OwnerShop });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CurrentBusinessId = null;
        var gone = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == owner.UserId);
        Assert.NotEqual(owner.Email.ToUpperInvariant(), gone.NormalizedEmail);
        Assert.StartsWith("DELETED.", gone.NormalizedEmail);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var again = await _client.PostAsJsonAsync(
            "/api/auth/register-business",
            new RegisterBusinessRequest(owner.Email, "Passw0rd!", "Ama", "Owusu", $"Reopen {suffix}", "+233200000001"),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Staff_DeleteAccount_FreesEmail_AndLeavesTheShopOpen()
    {
        var owner = await RegisterAsync("stf");
        var staff = await AddMemberAsync(owner.AccessToken, BusinessRole.Tailor);
        using var deleted = await SendAsync(HttpMethod.Post, "/api/me/delete-account", staff.AccessToken,
            new DeleteAccountRequest { Password = "Kofi1234", Confirmation = " delete " });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var staffMe = await SendAsync(HttpMethod.Get, "/api/me", staff.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, staffMe.StatusCode);

        using var ownerMe = await SendAsync(HttpMethod.Get, "/api/me", owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, ownerMe.StatusCode);

        using var again = await _client.PostAsJsonAsync(
            "/api/auth/register-business",
            new RegisterBusinessRequest(staff.Email, "Passw0rd!", "Kofi", "Boateng", $"Staff shop {Guid.NewGuid():N}"[..24], "+233200000002"),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Staff_WithAnotherShop_KeepsThatSignIn_WhenTheyDeleteHere()
    {
        var shopA = await RegisterAsync("a2");
        var shopB = await RegisterAsync("b2");
        var staff = await AddMemberAsync(shopA.AccessToken, BusinessRole.Tailor);
        await AddMembershipAsync(shopB.BusinessId, staff.UserId, BusinessRole.Tailor);

        using var deleted = await SendAsync(HttpMethod.Post, "/api/me/delete-account", staff.AccessToken,
            new DeleteAccountRequest { Password = "Kofi1234", Confirmation = DeleteAccountConfirmations.Staff });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(staff.Email, "Kofi1234"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var payload = (await login.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        Assert.Equal(shopB.BusinessId, payload.User!.BusinessId);

        using var shopAStill = await SendAsync(HttpMethod.Get, "/api/me", shopA.AccessToken);
        Assert.Equal(HttpStatusCode.OK, shopAStill.StatusCode);
    }

    [Fact]
    public async Task Owner_WhoAlsoWorksElsewhere_KeepsTheOtherShop_WhenTheyCloseThisOne()
    {
        var shopA = await RegisterAsync("oa");
        var shopB = await RegisterAsync("ob");
        await AddMembershipAsync(shopB.BusinessId, shopA.UserId, BusinessRole.Tailor);

        using var deleted = await SendAsync(HttpMethod.Post, "/api/me/delete-account", shopA.AccessToken,
            new DeleteAccountRequest { Password = "Passw0rd!", Confirmation = DeleteAccountConfirmations.OwnerShop });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var me = await SendAsync(HttpMethod.Get, "/api/me", shopA.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        using var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(shopA.Email, "Passw0rd!"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var payload = (await login.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        Assert.Equal(shopB.BusinessId, payload.User!.BusinessId);
        Assert.NotEqual("Deleted", payload.User.FirstName);
    }

    [Fact]
    public async Task ClosedShop_BlocksWrites_WhenAContextStillTargetsIt()
    {
        var owner = await RegisterAsync("wr");
        using var deleted = await SendAsync(HttpMethod.Post, "/api/me/delete-account", owner.AccessToken,
            new DeleteAccountRequest { Password = "Passw0rd!", Confirmation = DeleteAccountConfirmations.OwnerShop });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CurrentBusinessId = owner.BusinessId;
        db.Customers.Add(Customer.Create(
            IdGenerator.NewId(), owner.BusinessId, "Leftover", "+233200000099", null, null, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<NeedleBase.Application.Contracts.Auth.ShopClosedException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SecondOwnerDelete_AfterShopClosed_DoesNotPartialUpdate()
    {
        var owner = await RegisterAsync("dup");
        using var first = await SendAsync(HttpMethod.Post, "/api/me/delete-account", owner.AccessToken,
            new DeleteAccountRequest { Password = "Passw0rd!", Confirmation = DeleteAccountConfirmations.OwnerShop });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        using var second = await SendAsync(HttpMethod.Post, "/api/me/delete-account", owner.AccessToken,
            new DeleteAccountRequest { Password = "Passw0rd!", Confirmation = DeleteAccountConfirmations.OwnerShop });
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    private async Task<(string UserId, string AccessToken, string RefreshToken, string BusinessId, string Email)> RegisterAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"del{label}{suffix}@ama-stitches.test";
        using var response = await _client.PostAsJsonAsync(
            "/api/auth/register-business",
            new RegisterBusinessRequest(email, "Passw0rd!", "Ama", "Owusu", $"Delete {label} {suffix}", "+233200000000"),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        return (payload.User!.UserId, payload.AccessToken!, payload.RefreshToken!, payload.User.BusinessId, email);
    }

    private async Task<(string UserId, string AccessToken, string RefreshToken, string Email)> AddMemberAsync(string ownerToken, string role)
    {
        var email = $"m{Guid.NewGuid():N}"[..16] + "@ama-stitches.test";
        using var inviteResponse = await SendAsync(HttpMethod.Post, "/api/staff/invitations", ownerToken,
            new CreateInvitationRequest { Email = email, Role = role, Name = "Kofi Boateng" });
        Assert.Equal(HttpStatusCode.OK, inviteResponse.StatusCode);
        var invite = (await inviteResponse.Content.ReadFromJsonAsync<CreatedInvitationDto>(JsonOptions))!;
        using var accepted = await _client.PostAsJsonAsync("/api/auth/accept-invite",
            new AcceptInviteRequest { Code = invite.Code, FirstName = "Kofi", Surname = "Boateng", Password = "Kofi1234" },
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var payload = (await accepted.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        return (payload.User!.UserId, payload.AccessToken!, payload.RefreshToken!, email);
    }

    private async Task AddMembershipAsync(string businessId, string userId, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CurrentBusinessId = null;
        db.StaffMemberships.Add(StaffMembership.Create(IdGenerator.NewId(), businessId, userId, role, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed record AuthPayload(string? AccessToken, string? RefreshToken, AuthUserDto? User);
}
