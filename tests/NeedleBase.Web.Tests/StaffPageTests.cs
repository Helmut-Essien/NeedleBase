using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;
using StaffIndex = NeedleBase.Web.Pages.Staff.IndexModel;

namespace NeedleBase.Web.Tests;

public class StaffPageTests
{
    [Fact]
    public async Task Tailor_never_calls_the_staff_api()
    {
        var api = new FakeApi();
        var page = Pages.With(new StaffIndex(api), BusinessRole.Tailor);
        Assert.IsType<PageResult>(await page.OnGetAsync(default));
        Assert.DoesNotContain("staff", api.Calls);
        Assert.False(page.Loaded);
        Assert.NotNull(page.PageError);
    }

    [Fact]
    public async Task Manager_sees_staff_but_cannot_invite_or_change()
    {
        var api = new FakeApi();
        var page = Pages.With(new StaffIndex(api), BusinessRole.Manager);
        await page.OnGetAsync(default);
        Assert.True(page.Loaded);
        Assert.False(page.CanManage);

        page.Invite = new StaffIndex.InviteInput { Email = "a@b.co", Role = BusinessRole.Tailor };
        await page.OnPostInviteAsync(default);
        await page.OnPostDeactivateAsync("u2", default);
        Assert.DoesNotContain("invite", api.Calls);
        Assert.DoesNotContain(api.Calls, c => c.StartsWith("active:"));
    }

    [Fact]
    public async Task Owner_invite_shows_the_code_once_on_the_same_page()
    {
        var api = new FakeApi
        {
            Invite = r => ApiResult<CreatedInvitationDto>.Ok(new CreatedInvitationDto(
                new InvitationDto("i1", r.Email, null, r.Role, "Pending", Data.Modified, Data.Modified.AddDays(7), null), "ABCD-EFGH-JKMN"))
        };
        var page = Pages.With(new StaffIndex(api), BusinessRole.Owner);
        page.Invite = new StaffIndex.InviteInput { Email = "ama@shop.gh", Role = BusinessRole.Tailor };
        Assert.IsType<PageResult>(await page.OnPostInviteAsync(default));
        Assert.Equal("ABCD-EFGH-JKMN", page.NewInvite?.Code);
        Assert.Null(page.Flash);
        Assert.Equal(string.Empty, page.Invite.Email);
    }

    [Fact]
    public async Task Owner_invite_rejects_owner_role_without_calling_api()
    {
        var api = new FakeApi();
        var page = Pages.With(new StaffIndex(api), BusinessRole.Owner);
        page.Invite = new StaffIndex.InviteInput { Email = "ama@shop.gh", Role = BusinessRole.Owner };
        await page.OnPostInviteAsync(default);
        Assert.DoesNotContain("invite", api.Calls);
        Assert.NotNull(page.ActionError);
    }

    [Fact]
    public async Task Api_error_on_invite_is_shown_and_page_still_loads()
    {
        var api = new FakeApi { Invite = _ => Data.Problem<CreatedInvitationDto>(HttpStatusCode.Conflict, "INVITE_EMAIL_IN_USE", "That email already has a NeedleBase account.") };
        var page = Pages.With(new StaffIndex(api), BusinessRole.Owner);
        page.Invite = new StaffIndex.InviteInput { Email = "ama@shop.gh", Role = BusinessRole.Manager };
        await page.OnPostInviteAsync(default);
        Assert.NotNull(page.ActionError);
        Assert.Null(page.PageError);
        Assert.True(page.Loaded);
    }

    [Fact]
    public async Task Revoke_redirects_with_flash()
    {
        var api = new FakeApi();
        var page = Pages.With(new StaffIndex(api), BusinessRole.Owner);
        Assert.IsType<RedirectToPageResult>(await page.OnPostRevokeAsync("i1", default));
        Assert.Contains("revoke:i1", api.Calls);
        Assert.NotNull(page.Flash);
    }
}

public class TailorStatusRuleTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("u1", true)]
    [InlineData("u2", false)]
    public async Task Tailor_status_control_only_on_own_or_unassigned(string? assignee, bool expected)
    {
        var api = new FakeApi { Order = id => ApiResult<NeedleBase.Application.Contracts.Orders.OrderDetailDto>.Ok(Data.Detail(id, "New", assignee)) };
        var page = Pages.With(new NeedleBase.Web.Pages.Orders.DetailsModel(api), BusinessRole.Tailor);
        page.Id = "o1";
        await page.OnGetAsync(default);
        Assert.Equal(expected, page.CanChangeStatus);
    }
}
