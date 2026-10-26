using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>Slice 7: invites, accept, roles, access, password change, tailor limits (Journey 5).</summary>
public sealed partial class StaffApiTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly NeedleBaseApiFactory _factory;
    private HttpClient _client; // swapped by the email tests (one test class instance per test)

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public StaffApiTests(NeedleBaseApiFactory factory, Xunit.Abstractions.ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Journey5_OwnerInvitesKofiAsTailor_KofiJoins_WithTailorLimits()
    {
        var owner = await RegisterAsync("j5");

        // Solo business: works with zero staff, Staff screen shows only the owner.
        var solo = await OverviewAsync(owner.Token);
        Assert.Single(solo.Members);
        Assert.True(solo.Members[0].IsYou);
        Assert.True(solo.CanManage);
        Assert.Empty(solo.Invitations);

        var kofiEmail = $"kofi{Guid.NewGuid():N}"[..20] + "@ama-stitches.test";
        var created = await InviteAsync(owner.Token, kofiEmail, BusinessRole.Tailor, "Kofi");
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", created.Code);
        Assert.Equal(InvitationStatus.Pending, created.Invitation.Status);

        // Preview: lower case and no dashes still work.
        using (var preview = await _client.PostAsJsonAsync("/api/auth/invite-preview",
                   new InviteCodeRequest { Code = created.Code.Replace("-", "").ToLowerInvariant() }, JsonOptions))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var p = (await preview.Content.ReadFromJsonAsync<InvitePreviewDto>(JsonOptions))!;
            Assert.Equal(BusinessRole.Tailor, p.Role);
            Assert.Equal(kofiEmail, p.Email);
            Assert.StartsWith("Staff j5", p.BusinessName);
        }

        var kofi = await AcceptAsync(created.Code, "Kofi", "Boateng", "Kofi1234");
        Assert.Equal(HttpStatusCode.OK, kofi.Status);
        Assert.Equal(BusinessRole.Tailor, kofi.Payload!.User!.Role);
        Assert.Equal(owner.BusinessId, kofi.Payload.User.BusinessId);
        var kofiToken = kofi.Payload.AccessToken!;

        // Same code again: used.
        var again = await AcceptAsync(created.Code, "Kofi", "Boateng", "Kofi1234");
        Assert.Equal(StaffErrorMessages.InviteUsed, again.Code);

        // Web shows Accepted.
        var after = await OverviewAsync(owner.Token);
        Assert.Equal(InvitationStatus.Accepted, after.Invitations.Single().Status);
        Assert.Contains(after.Members, m => m.Email == kofiEmail && m.Role == BusinessRole.Tailor && m.IsActive);

        // Kofi: no Staff, no payments, no inviting; names still work for assignee labels.
        await AssertStatus(HttpStatusCode.Forbidden, HttpMethod.Get, "/api/staff", kofiToken);
        await AssertStatus(HttpStatusCode.Forbidden, HttpMethod.Post, "/api/staff/invitations", kofiToken,
            new CreateInvitationRequest { Email = "x@ama-stitches.test", Role = BusinessRole.Tailor });
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Get, "/api/staff/names", kofiToken);

        // Assigned work: Kofi moves his own order and unassigned ones, not someone else's.
        var mine = await CreateOrderAsync(owner.Token, kofi.Payload.User.UserId);
        var unassigned = await CreateOrderAsync(owner.Token, null);
        var ownersOwn = await CreateOrderAsync(owner.Token, after.Members.Single(m => m.IsYou).UserId);
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(kofiToken, mine, OrderStatus.Cutting));
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(kofiToken, unassigned, OrderStatus.Cutting));
        using (var refused = await SendAsync(HttpMethod.Patch, $"/api/orders/{ownersOwn}/status", kofiToken,
                   new UpdateOrderStatusRequest { Status = OrderStatus.Cutting }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(OrderErrorMessages.NotAssigned, await CodeAsync(refused));
        }

        var counts = await OverviewAsync(owner.Token);
        Assert.Equal(1, counts.Members.Single(m => m.Email == kofiEmail).OpenAssignedOrders);
    }

    [Fact]
    public async Task RoleChange_TakesEffectAtOnce_AndRefreshCarriesTheNewRole()
    {
        var owner = await RegisterAsync("role");
        var (kofiId, kofiTokens) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);

        using (var changed = await SendAsync(HttpMethod.Put, $"/api/staff/{kofiId}/role", owner.Token,
                   new ChangeStaffRoleRequest { Role = BusinessRole.Manager }))
        {
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            Assert.Equal(BusinessRole.Manager, (await changed.Content.ReadFromJsonAsync<StaffMemberDto>(JsonOptions))!.Role);
        }

        // Old access token (Tailor) is dead; refresh gives Manager.
        await AssertStatus(HttpStatusCode.Unauthorized, HttpMethod.Get, "/api/staff/names", kofiTokens.AccessToken!);
        var refreshed = await RefreshAsync(kofiTokens.RefreshToken!);
        Assert.Equal(BusinessRole.Manager, refreshed.User!.Role);
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Get, "/api/staff", refreshed.AccessToken!);
        // Managers view but don't change staff.
        await AssertStatus(HttpStatusCode.Forbidden, HttpMethod.Put, $"/api/staff/{kofiId}/role", refreshed.AccessToken!,
            new ChangeStaffRoleRequest { Role = BusinessRole.Tailor });

        // Owner can't be changed, can't change self, Owner role can't be given.
        var ownerId = (await OverviewAsync(owner.Token)).Members.Single(m => m.IsYou).UserId;
        await AssertCode(StaffErrorMessages.StaffSelf, HttpMethod.Put, $"/api/staff/{ownerId}/role", owner.Token,
            new ChangeStaffRoleRequest { Role = BusinessRole.Tailor });
        await AssertCode(StaffErrorMessages.StaffSelf, HttpMethod.Post, $"/api/staff/{ownerId}/deactivate", owner.Token);
        await AssertCode(StaffErrorMessages.RoleInvalid, HttpMethod.Put, $"/api/staff/{kofiId}/role", owner.Token,
            new ChangeStaffRoleRequest { Role = BusinessRole.Owner });
        await AssertCode(StaffErrorMessages.StaffNotFound, HttpMethod.Put, "/api/staff/01J0000000000000000000NOPE/role", owner.Token,
            new ChangeStaffRoleRequest { Role = BusinessRole.Tailor });
    }

    [Fact]
    public async Task Deactivate_SignsTheMemberOut_WithNoMembership_AndReactivateLetsThemBackIn()
    {
        var owner = await RegisterAsync("off");
        var (kofiId, kofiTokens) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);

        await AssertStatus(HttpStatusCode.OK, HttpMethod.Post, $"/api/staff/{kofiId}/deactivate", owner.Token);
        await AssertStatus(HttpStatusCode.Unauthorized, HttpMethod.Get, "/api/staff/names", kofiTokens.AccessToken!);
        using (var refresh = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(string.Empty, kofiTokens.RefreshToken!), JsonOptions))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            Assert.Equal("NO_MEMBERSHIP", await CodeAsync(refresh)); // the phone wipes its copy on this code
        }

        using (var blocked = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(kofiTokens.User!.Email, "Kofi1234"), JsonOptions))
        {
            Assert.Equal("ACCOUNT_DEACTIVATED", await CodeAsync(blocked)); // "turned off, ask your owner", not "no business"
        }

        var overview = await OverviewAsync(owner.Token);
        Assert.False(overview.Members.Single(m => m.UserId == kofiId).IsActive);

        await AssertStatus(HttpStatusCode.OK, HttpMethod.Post, $"/api/staff/{kofiId}/reactivate", owner.Token);
        using var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(kofiTokens.User!.Email, "Kofi1234"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // Audit: staff.* kept.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = await db.AuditEvents.IgnoreQueryFilters().Where(a => a.BusinessId == owner.BusinessId).Select(a => a.Action).ToListAsync();
        Assert.Contains(AuditActions.StaffInvited, actions);
        Assert.Contains(AuditActions.StaffAdded, actions);
        Assert.Contains(AuditActions.StaffDeactivated, actions);
        Assert.Contains(AuditActions.StaffReactivated, actions);
    }

    [Fact]
    public async Task Invites_Validation_Reinvite_Revoke_Expiry_Tenancy()
    {
        var owner = await RegisterAsync("inv");
        var other = await RegisterAsync("inv2");

        // Existing account email, bad role, bad email.
        await AssertCode(StaffErrorMessages.InviteEmailInUse, HttpMethod.Post, "/api/staff/invitations", owner.Token,
            new CreateInvitationRequest { Email = other.Email, Role = BusinessRole.Tailor });
        await AssertCode(StaffErrorMessages.RoleInvalid, HttpMethod.Post, "/api/staff/invitations", owner.Token,
            new CreateInvitationRequest { Email = "ok@ama-stitches.test", Role = BusinessRole.Owner });
        await AssertStatus(HttpStatusCode.BadRequest, HttpMethod.Post, "/api/staff/invitations", owner.Token,
            new CreateInvitationRequest { Email = "not-an-email", Role = BusinessRole.Tailor });

        // Re-invite same email replaces the old code.
        var email = $"esi{Guid.NewGuid():N}"[..16] + "@ama-stitches.test";
        var first = await InviteAsync(owner.Token, email, BusinessRole.Tailor, null);
        var second = await InviteAsync(owner.Token, email.ToUpperInvariant(), BusinessRole.Manager, null);
        Assert.Equal(StaffErrorMessages.InviteRevoked, (await AcceptAsync(first.Code, "Esi", "Ansah", "Esi12345")).Code);

        // Another business can't see or revoke it.
        await AssertCode(StaffErrorMessages.InviteNotFound, HttpMethod.Delete, $"/api/staff/invitations/{second.Invitation.Id}", other.Token);
        Assert.Empty((await OverviewAsync(other.Token)).Invitations);

        // Revoke, then the code is refused.
        await AssertStatus(HttpStatusCode.NoContent, HttpMethod.Delete, $"/api/staff/invitations/{second.Invitation.Id}", owner.Token);
        await AssertStatus(HttpStatusCode.NoContent, HttpMethod.Delete, $"/api/staff/invitations/{second.Invitation.Id}", owner.Token);
        Assert.Equal(StaffErrorMessages.InviteRevoked, (await AcceptAsync(second.Code, "Esi", "Ansah", "Esi12345")).Code);

        // Expired.
        var third = await InviteAsync(owner.Token, "x" + email, BusinessRole.Tailor, null);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Invitations\" SET \"ExpiresAtUtc\" = now() - interval '1 minute' WHERE \"Id\" = {third.Invitation.Id}");
        }

        Assert.Equal(StaffErrorMessages.InviteExpired, (await AcceptAsync(third.Code, "Esi", "Ansah", "Esi12345")).Code);
        Assert.Equal(InvitationStatus.Expired, (await OverviewAsync(owner.Token)).Invitations.Single(i => i.Id == third.Invitation.Id).Status);

        // Wrong code and weak password.
        Assert.Equal(StaffErrorMessages.InviteNotFound, (await AcceptAsync("AAAA-BBBB-CCCC", "Esi", "Ansah", "Esi12345")).Code);
        var fourth = await InviteAsync(owner.Token, "y" + email, BusinessRole.Tailor, null);
        Assert.Equal(StaffErrorMessages.PasswordWeak, (await AcceptAsync(fourth.Code, "Esi", "Ansah", "abcdefgh")).Code);
        Assert.Equal(HttpStatusCode.OK, (await AcceptAsync(fourth.Code, "Esi", "Ansah", "Esi12345")).Status);
    }

    [Fact]
    public async Task ConcurrentAccepts_SameCode_CreateOneAccount()
    {
        var owner = await RegisterAsync("race");
        var email = $"race{Guid.NewGuid():N}"[..16] + "@ama-stitches.test";
        var invite = await InviteAsync(owner.Token, email, BusinessRole.Tailor, null);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AcceptAsync(invite.Code, "Ama", "Twin", "Twin1234")));
        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.Status != HttpStatusCode.OK),
            r => Assert.Contains(r.Code, new[] { StaffErrorMessages.InviteUsed, StaffErrorMessages.InviteEmailInUse }));
        Assert.Equal(2, (await OverviewAsync(owner.Token)).Members.Count);
    }

    [Fact]
    public async Task ChangePassword_ChecksCurrent_SignsOutOtherDevices_AndIsAudited()
    {
        var owner = await RegisterAsync("pwd");
        using var secondLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(owner.Email, "Passw0rd!"), JsonOptions);
        var other = (await secondLogin.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;

        await AssertCode(StaffErrorMessages.PasswordIncorrect, HttpMethod.Post, "/api/auth/change-password", owner.Token,
            new ChangePasswordRequest { CurrentPassword = "nope12345", NewPassword = "Newpass123" });
        await AssertCode(StaffErrorMessages.PasswordWeak, HttpMethod.Post, "/api/auth/change-password", owner.Token,
            new ChangePasswordRequest { CurrentPassword = "Passw0rd!", NewPassword = "abcdefgh" });

        using var ok = await SendAsync(HttpMethod.Post, "/api/auth/change-password", owner.Token,
            new ChangePasswordRequest { CurrentPassword = "Passw0rd!", NewPassword = "Newpass123" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var fresh = (await ok.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;

        await AssertStatus(HttpStatusCode.Unauthorized, HttpMethod.Get, "/api/staff/names", owner.Token);
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Get, "/api/staff/names", fresh.AccessToken!);
        using (var oldRefresh = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(string.Empty, other.RefreshToken!), JsonOptions))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
            Assert.Equal("REFRESH_INVALID", await CodeAsync(oldRefresh)); // not "reuse": no false theft alarm
        }

        using var newLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(owner.Email, "Newpass123"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var events = await db.AuditEvents.IgnoreQueryFilters()
            .Where(a => a.BusinessId == owner.BusinessId && a.Action == AuditActions.PasswordChanged).ToListAsync();
        var e = Assert.Single(events);
        Assert.DoesNotContain("Newpass123", e.Summary);
        Assert.DoesNotContain("Passw0rd", e.Summary);
    }

    // ---- helpers ----

    private async Task<(string UserId, AuthPayload Tokens)> AddMemberAsync(string ownerToken, string role)
    {
        var email = $"m{Guid.NewGuid():N}"[..16] + "@ama-stitches.test";
        var invite = await InviteAsync(ownerToken, email, role, null);
        var accepted = await AcceptAsync(invite.Code, "Kofi", "Boateng", "Kofi1234");
        Assert.Equal(HttpStatusCode.OK, accepted.Status);
        return (accepted.Payload!.User!.UserId, accepted.Payload);
    }

    private async Task<AuthPayload> RefreshAsync(string refreshToken)
    {
        using var response = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(string.Empty, refreshToken), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
    }

    private async Task<StaffOverviewDto> OverviewAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/staff", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StaffOverviewDto>(JsonOptions))!;
    }

    private async Task<CreatedInvitationDto> InviteAsync(string token, string email, string role, string? name)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/staff/invitations", token,
            new CreateInvitationRequest { Email = email, Role = role, Name = name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedInvitationDto>(JsonOptions))!;
    }

    private async Task<(HttpStatusCode Status, AuthPayload? Payload, string? Code)> AcceptAsync(string code, string first, string surname, string password)
    {
        using var response = await _client.PostAsJsonAsync("/api/auth/accept-invite",
            new AcceptInviteRequest { Code = code, FirstName = first, Surname = surname, Password = password }, JsonOptions);
        return response.IsSuccessStatusCode
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions), null)
            : (response.StatusCode, null, await CodeAsync(response));
    }

    private async Task<HttpStatusCode> SetStatusAsync(string token, string orderId, string status)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"/api/orders/{orderId}/status", token, new UpdateOrderStatusRequest { Status = status });
        return response.StatusCode;
    }

    private async Task<string> CreateOrderAsync(string token, string? assignee)
    {
        using var customerResponse = await SendAsync(HttpMethod.Post, "/api/customers", token,
            new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;
        using var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new CreateOrderRequest
        {
            CustomerId = customer.Id,
            TotalAmount = 300,
            AssignedToUserId = assignee,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = 300,
                    Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!.Id;
    }

    private async Task AssertCode(string expected, HttpMethod method, string url, string token, object? body = null)
    {
        using var response = await SendAsync(method, url, token, body);
        Assert.Equal(expected, await CodeAsync(response));
    }

    private async Task AssertStatus(HttpStatusCode expected, HttpMethod method, string url, string token, object? body = null)
    {
        using var response = await SendAsync(method, url, token, body);
        Assert.True(response.StatusCode == expected, $"{method} {url} returned {(int)response.StatusCode}, expected {(int)expected}.");
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

    private async Task<(string Token, string BusinessId, string Email)> RegisterAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"staff{label}{suffix}@ama-stitches.test";
        using var response = await _client.PostAsJsonAsync("/api/auth/register-business",
            new RegisterBusinessRequest(email, "Passw0rd!", "Ama", "Owusu", $"Staff {label} {suffix}", "+233200000000"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        return (payload.AccessToken!, payload.User!.BusinessId, email);
    }

    private sealed record AuthPayload(string? AccessToken, string? RefreshToken, AuthUserDto? User);
}
