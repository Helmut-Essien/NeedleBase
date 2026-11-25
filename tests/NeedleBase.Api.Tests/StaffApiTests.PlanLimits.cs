using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

public sealed partial class StaffApiTests
{
    [Fact]
    public async Task Starter_plan_refuses_third_staff_invite()
    {
        var owner = await RegisterAsync("cap");
        await SetPlanAsync(owner.BusinessId, PlanCodes.Starter);

        await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        await AddMemberAsync(owner.Token, BusinessRole.Manager);

        using var response = await SendAsync(HttpMethod.Post, "/api/staff/invitations", owner.Token,
            new CreateInvitationRequest
            {
                Email = $"over{Guid.NewGuid():N}"[..16] + "@ama-stitches.test",
                Role = BusinessRole.Tailor
            });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(StaffErrorMessages.StaffLimit, await CodeAsync(response));
    }

    [Fact]
    public async Task Starter_pending_invite_counts_toward_cap()
    {
        var owner = await RegisterAsync("pend");
        await SetPlanAsync(owner.BusinessId, PlanCodes.Starter);
        await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        await InviteAsync(owner.Token, $"hold{Guid.NewGuid():N}"[..16] + "@ama-stitches.test", BusinessRole.Manager, null);

        using var response = await SendAsync(HttpMethod.Post, "/api/staff/invitations", owner.Token,
            new CreateInvitationRequest
            {
                Email = $"extra{Guid.NewGuid():N}"[..16] + "@ama-stitches.test",
                Role = BusinessRole.Tailor
            });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(StaffErrorMessages.StaffLimit, await CodeAsync(response));
    }

    [Fact]
    public async Task Accept_invite_refused_when_plan_staff_cap_already_full()
    {
        var owner = await RegisterAsync("full");
        // Keyless: create two staff + one pending invite, then lock to Starter before accept.
        await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        await AddMemberAsync(owner.Token, BusinessRole.Manager);
        var pending = await InviteAsync(owner.Token, $"late{Guid.NewGuid():N}"[..16] + "@ama-stitches.test", BusinessRole.Tailor, null);

        await SetPlanAsync(owner.BusinessId, PlanCodes.Starter);

        var accepted = await AcceptAsync(pending.Code, "Ama", "Mensah", "Ama12345");
        Assert.Equal(HttpStatusCode.Conflict, accepted.Status);
        Assert.Equal(StaffErrorMessages.StaffLimit, accepted.Code);
    }

    [Fact]
    public async Task Keyless_shop_stays_unlimited_for_staff()
    {
        var owner = await RegisterAsync("keyless");
        for (var i = 0; i < 3; i++)
        {
            await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        }

        var overview = await OverviewAsync(owner.Token);
        Assert.Equal(4, overview.Members.Count); // owner + 3
    }

    [Fact]
    public async Task Reactivate_refused_when_plan_staff_cap_full()
    {
        var owner = await RegisterAsync("re");
        await SetPlanAsync(owner.BusinessId, PlanCodes.Starter);

        var (firstId, _) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        var (secondId, _) = await AddMemberAsync(owner.Token, BusinessRole.Manager);

        using (var off = await SendAsync(HttpMethod.Post, $"/api/staff/{firstId}/deactivate", owner.Token))
        {
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        }

        await AddMemberAsync(owner.Token, BusinessRole.Tailor); // fills the free seat

        using var response = await SendAsync(HttpMethod.Post, $"/api/staff/{firstId}/reactivate", owner.Token);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(StaffErrorMessages.StaffLimit, await CodeAsync(response));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/staff", owner.Token)).StatusCode);
        _ = secondId;
    }

    [Fact]
    public async Task Downgrade_keeps_existing_staff_but_blocks_new_invites()
    {
        var owner = await RegisterAsync("down");
        await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        await AddMemberAsync(owner.Token, BusinessRole.Manager);
        await AddMemberAsync(owner.Token, BusinessRole.Tailor); // 3 staff while keyless

        await SetPlanAsync(owner.BusinessId, PlanCodes.Starter);

        var overview = await OverviewAsync(owner.Token);
        Assert.Equal(4, overview.Members.Count(m => m.IsActive)); // owner + 3 kept

        using var response = await SendAsync(HttpMethod.Post, "/api/staff/invitations", owner.Token,
            new CreateInvitationRequest
            {
                Email = $"more{Guid.NewGuid():N}"[..16] + "@ama-stitches.test",
                Role = BusinessRole.Tailor
            });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(StaffErrorMessages.StaffLimit, await CodeAsync(response));
    }

    private async Task SetPlanAsync(string businessId, string planCode, bool complimentary = false, bool trial = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var business = await db.Businesses.SingleAsync(b => b.Id == businessId);
        business.AttachLicense(
            "protected-test-key",
            "…TEST",
            new LicenseCheck(true, "ok", "lic-plan", planCode, trial, complimentary),
            DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }
}
