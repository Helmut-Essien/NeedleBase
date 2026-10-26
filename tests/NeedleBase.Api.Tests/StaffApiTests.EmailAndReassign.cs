using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Email;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>Captures mail instead of sending it (no SMTP server or credentials in tests).</summary>
public sealed class CapturingTransport : IEmailTransport
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public async Task<EmailMessage> WaitForAsync(string to, string kind)
    {
        for (var i = 0; i < 100; i++)
        {
            var hit = Sent.LastOrDefault(m => m.ToAddress == to && m.Kind == kind);
            if (hit is not null)
            {
                return hit;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"No {kind} email to {to}.");
    }
}

public sealed partial class StaffApiTests
{
    private CapturingTransport UseEmail()
    {
        var capture = new CapturingTransport();
        var factory = _factory.WithWebHostBuilder(b =>
        {
            // Placeholder values: the capturing transport never connects anywhere.
            b.UseSetting("Email:Host", "smtp.example.test");
            b.UseSetting("Email:Username", "shop@example.test");
            b.UseSetting("Email:Password", "not-a-real-password");
            b.UseSetting("Email:AppBaseUrl", "https://app.example.test");
            b.ConfigureTestServices(s => s.AddSingleton<IEmailTransport>(capture));
        });
        _client = factory.CreateClient();
        return capture;
    }

    [Fact]
    public async Task EmailOff_NoForgotPassword_InvitesStillWorkByHand()
    {
        var options = await _client.GetFromJsonAsync<AuthOptionsDto>("/api/auth/options", JsonOptions);
        Assert.False(options!.EmailEnabled);

        using (var forgot = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = "a@ama-stitches.test" }, JsonOptions))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, forgot.StatusCode);
            Assert.Equal(StaffErrorMessages.ResetUnavailable, await CodeAsync(forgot));
        }

        using (var reset = await _client.PostAsJsonAsync("/api/auth/reset-password",
                   new ResetPasswordRequest { Email = "a@ama-stitches.test", Code = "12345678", NewPassword = "abc12345" }, JsonOptions))
        {
            Assert.Equal(StaffErrorMessages.ResetUnavailable, await CodeAsync(reset));
        }

        var owner = await RegisterAsync("noemail");
        var invite = await InviteAsync(owner.Token, $"n{Guid.NewGuid():N}"[..12] + "@ama-stitches.test", BusinessRole.Tailor, null);
        Assert.False(invite.Emailed);
        Assert.False(string.IsNullOrEmpty(invite.Code));
    }

    [Fact]
    public async Task EmailOn_InviteIsEmailed_WithTheCode_AndManualCodeStillShown()
    {
        var mail = UseEmail();
        Assert.True((await _client.GetFromJsonAsync<AuthOptionsDto>("/api/auth/options", JsonOptions))!.EmailEnabled);

        var owner = await RegisterAsync("mailinv");
        var to = $"e{Guid.NewGuid():N}"[..12] + "@ama-stitches.test";
        var invite = await InviteAsync(owner.Token, to, BusinessRole.Tailor, "Esi <b>");
        Assert.True(invite.Emailed);

        var message = await mail.WaitForAsync(to, "invite");
        Assert.Contains(invite.Code, message.TextBody);
        Assert.Contains(invite.Code, message.HtmlBody);
        Assert.Contains("https://app.example.test/Account/AcceptInvite", message.TextBody);
        Assert.Contains("Esi &lt;b&gt;", message.HtmlBody); // names are HTML-encoded
        Assert.Contains("GMT", message.TextBody);
    }

    [Fact]
    public async Task PasswordReset_EndToEnd_SingleUse_NoLeak_SignsOutEverywhere_AuditedWithoutSecrets()
    {
        var mail = UseEmail();
        var owner = await RegisterAsync("reset");
        using var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(owner.Email, "Passw0rd!"), JsonOptions);
        var device = (await login.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;

        // Unknown email: same 202, nothing sent.
        var nobody = $"nobody{Guid.NewGuid():N}@ama-stitches.test";
        using (var unknown = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = nobody }, JsonOptions))
        {
            Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        }

        using (var known = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = owner.Email.ToUpperInvariant() }, JsonOptions))
        {
            Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        }

        var message = await mail.WaitForAsync(owner.Email, "password_reset");
        Assert.DoesNotContain(mail.Sent, m => m.ToAddress == nobody);
        var code = Regex.Match(message.TextBody, @"\b(\d{4} \d{4})\b").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(code));
        var wrong = code == "0000 0000" ? "1111 1111" : "0000 0000";

        Assert.Equal(StaffErrorMessages.ResetCodeInvalid, await ResetAsync(owner.Email, wrong, "Fresh1234"));
        Assert.Equal(StaffErrorMessages.ResetCodeInvalid, await ResetAsync(nobody, code, "Fresh1234")); // same reply
        // Weak password: refused without using up the code.
        Assert.Equal(StaffErrorMessages.PasswordWeak, await ResetAsync(owner.Email, code, "ALLCAPS123"));
        Assert.Null(await ResetAsync(owner.Email, code.Replace(" ", ""), "Fresh1234"));
        Assert.Equal(StaffErrorMessages.ResetCodeInvalid, await ResetAsync(owner.Email, code, "Again1234")); // single use

        // Every session is gone; the new password works, the old one doesn't.
        await AssertStatus(HttpStatusCode.Unauthorized, HttpMethod.Get, "/api/staff/names", device.AccessToken!);
        using (var oldRefresh = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(string.Empty, device.RefreshToken!), JsonOptions))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        }

        using (var oldPw = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(owner.Email, "Passw0rd!"), JsonOptions))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oldPw.StatusCode);
        }

        using (var newPw = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(owner.Email, "Fresh1234"), JsonOptions))
        {
            Assert.Equal(HttpStatusCode.OK, newPw.StatusCode);
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var events = await db.AuditEvents.IgnoreQueryFilters()
            .Where(a => a.BusinessId == owner.BusinessId &&
                        (a.Action == AuditActions.PasswordResetRequested || a.Action == AuditActions.PasswordReset))
            .ToListAsync();
        Assert.Contains(events, e => e.Action == AuditActions.PasswordResetRequested);
        Assert.Contains(events, e => e.Action == AuditActions.PasswordReset);
        Assert.All(events, e =>
        {
            Assert.DoesNotContain(code.Replace(" ", ""), e.Summary.Replace(" ", ""));
            Assert.DoesNotContain("Fresh1234", e.Summary);
        });
        // Only a hash is stored.
        var stored = await db.PasswordResetCodes.Where(c => c.ApplicationUserId == device.User!.UserId).Select(c => c.CodeHash).ToListAsync();
        Assert.NotEmpty(stored);
        Assert.DoesNotContain(stored, h => h.Contains(code.Replace(" ", "")));
    }

    [Fact]
    public async Task PasswordReset_NewRequestKillsTheOldCode_AndTooManyWrongTriesKillTheCode()
    {
        var mail = UseEmail();
        var owner = await RegisterAsync("reset2");

        async Task<string> RequestAsync()
        {
            var before = mail.Sent.Count(m => m.ToAddress == owner.Email);
            using var r = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = owner.Email }, JsonOptions);
            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
            for (var i = 0; i < 100 && mail.Sent.Count(m => m.ToAddress == owner.Email) == before; i++)
            {
                await Task.Delay(50);
            }

            var m = mail.Sent.Last(x => x.ToAddress == owner.Email);
            return Regex.Match(m.TextBody, @"\b(\d{4} \d{4})\b").Groups[1].Value;
        }

        var first = await RequestAsync();
        var second = await RequestAsync();
        if (first != second)
        {
            Assert.Equal(StaffErrorMessages.ResetCodeInvalid, await ResetAsync(owner.Email, first, "Fresh1234"));
        }

        var wrong = second == "0000 0000" ? "1111 1111" : "0000 0000";
        while (true)
        {
            using var r = await _client.PostAsJsonAsync("/api/auth/reset-password",
                new ResetPasswordRequest { Email = owner.Email, Code = wrong, NewPassword = "Fresh1234" }, JsonOptions);
            if (r.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.True(r.Headers.RetryAfter is not null);
                break; // per-IP slow-down after repeated wrong codes
            }

            Assert.Equal(StaffErrorMessages.ResetCodeInvalid, await CodeAsync(r));
        }

        // Per-code cap (another phone/IP): after MaxAttempts wrong tries even the right code is dead.
        mail = UseEmail(); // fresh host = fresh per-IP throttle; same database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userId = await db.Users.Where(u => u.Email == owner.Email).Select(u => u.Id).SingleAsync();
            await db.PasswordResetCodes.Where(c => c.ApplicationUserId == userId && c.UsedAtUtc == null)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.FailedAttempts, NeedleBase.Infrastructure.Identity.PasswordResetCode.MaxAttempts));
        }

        Assert.Equal(StaffErrorMessages.ResetCodeInvalid, await ResetAsync(owner.Email, second, "Fresh1234"));
    }

    [Fact]
    public async Task ReassignOpenOrders_OwnerOnly_Transactional_SkipsClosedOrders_Audited()
    {
        var owner = await RegisterAsync("move");
        var (kofiId, kofiTokens) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        var (amaId, amaTokens) = await AddMemberAsync(owner.Token, BusinessRole.Manager);

        var a = await CreateOrderAsync(owner.Token, kofiId);
        var b = await CreateOrderAsync(owner.Token, kofiId);
        var closed = await CreateOrderAsync(owner.Token, kofiId);
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, closed, OrderStatus.Cancelled));

        // Turned off: their open work still shows so the owner can move it.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/staff/{kofiId}/deactivate", owner.Token)).StatusCode);
        var overview = await OverviewAsync(owner.Token);
        Assert.Equal(2, overview.Members.Single(m => m.UserId == kofiId).OpenAssignedOrders);

        // Only the Owner.
        var amaFresh = await RefreshAsync(amaTokens.RefreshToken!);
        await AssertStatus(HttpStatusCode.Forbidden, HttpMethod.Post, $"/api/staff/{kofiId}/reassign-orders", amaFresh.AccessToken!,
            new ReassignOrdersRequest { ToUserId = amaId });

        // Bad targets: turned-off member, the same person, someone from another shop.
        await AssertCode(StaffErrorMessages.ReassignTargetInvalid, HttpMethod.Post, $"/api/staff/{amaId}/reassign-orders", owner.Token,
            new ReassignOrdersRequest { ToUserId = kofiId });
        await AssertCode(StaffErrorMessages.ReassignTargetInvalid, HttpMethod.Post, $"/api/staff/{amaId}/reassign-orders", owner.Token,
            new ReassignOrdersRequest { ToUserId = amaId });
        var other = await RegisterAsync("moveother");
        var otherOwnerId = (await OverviewAsync(other.Token)).Members.Single().UserId;
        await AssertCode(StaffErrorMessages.ReassignTargetInvalid, HttpMethod.Post, $"/api/staff/{kofiId}/reassign-orders", owner.Token,
            new ReassignOrdersRequest { ToUserId = otherOwnerId });
        await AssertCode(StaffErrorMessages.StaffNotFound, HttpMethod.Post, $"/api/staff/{otherOwnerId}/reassign-orders", owner.Token,
            new ReassignOrdersRequest());

        using (var moved = await SendAsync(HttpMethod.Post, $"/api/staff/{kofiId}/reassign-orders", owner.Token, new ReassignOrdersRequest { ToUserId = amaId }))
        {
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            var dto = (await moved.Content.ReadFromJsonAsync<ReassignOrdersResultDto>(JsonOptions))!;
            Assert.Equal(2, dto.Moved);
            Assert.Equal(amaId, dto.ToUserId);
            Assert.Equal("Kofi Boateng", dto.ToName);
        }

        var after = await OverviewAsync(owner.Token);
        Assert.Equal(0, after.Members.Single(m => m.UserId == kofiId).OpenAssignedOrders);
        Assert.Equal(2, after.Members.Single(m => m.UserId == amaId).OpenAssignedOrders);
        Assert.Equal(kofiId, (await OrderAsync(owner.Token, closed)).AssignedToUserId); // closed order untouched

        // Leave unassigned.
        using (var unassign = await SendAsync(HttpMethod.Post, $"/api/staff/{amaId}/reassign-orders", owner.Token, new ReassignOrdersRequest()))
        {
            Assert.Equal(2, (await unassign.Content.ReadFromJsonAsync<ReassignOrdersResultDto>(JsonOptions))!.Moved);
        }

        Assert.Null((await OrderAsync(owner.Token, a)).AssignedToUserId);
        Assert.Null((await OrderAsync(owner.Token, b)).AssignedToUserId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var summaries = await db.AuditEvents.IgnoreQueryFilters()
            .Where(x => x.BusinessId == owner.BusinessId && x.Action == AuditActions.StaffOrdersReassigned)
            .Select(x => x.Summary).ToListAsync();
        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, s => s.StartsWith("Moved 2 open orders", StringComparison.Ordinal));
        Assert.Contains(summaries, s => s.StartsWith("Unassigned 2 open orders", StringComparison.Ordinal));
        _ = kofiTokens;
    }

    private async Task<OrderDetailDto> OrderAsync(string token, string id)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/orders/{id}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!;
    }

    /// <summary>Null on success (204), else the problem code.</summary>
    private async Task<string?> ResetAsync(string email, string code, string password)
    {
        using var response = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest { Email = email, Code = code, NewPassword = password }, JsonOptions);
        return response.StatusCode == HttpStatusCode.NoContent ? null : await CodeAsync(response);
    }
}
