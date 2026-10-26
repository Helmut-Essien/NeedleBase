using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Pages.Account;
using NeedleBase.Web.Services;
using StaffIndex = NeedleBase.Web.Pages.Staff.IndexModel;

namespace NeedleBase.Web.Tests;

public class PasswordResetAndReassignTests
{
    private sealed class FakeAuth : IApiAuthService
    {
        public bool Enabled { get; set; } = true;
        public bool Result { get; set; } = true;
        public List<string> Calls { get; } = new();
        public string? LastErrorMessage { get; set; }
        public Task<bool> EmailEnabledAsync(CancellationToken c = default) => Task.FromResult(Enabled);
        public Task<bool> RequestPasswordResetAsync(string email, CancellationToken c = default) { Calls.Add("ask:" + email); return Task.FromResult(Result); }
        public Task<bool> ResetPasswordAsync(ResetPasswordRequest r, CancellationToken c = default) { Calls.Add($"reset:{r.Email}:{r.Code}"); return Task.FromResult(Result); }
        public Task<AuthSessionResult?> LoginAsync(LoginRequest r, CancellationToken c = default) => throw new NotSupportedException();
        public Task<AuthSessionResult?> RegisterBusinessAsync(RegisterBusinessRequest r, CancellationToken c = default) => throw new NotSupportedException();
        public Task<TokenResponse?> RefreshAsync(string a, string r, CancellationToken c = default) => throw new NotSupportedException();
        public Task LogoutAsync(string? r, CancellationToken c = default) => Task.CompletedTask;
        public Task<InvitePreviewDto?> PreviewInviteAsync(string code, CancellationToken c = default) => throw new NotSupportedException();
        public Task<AuthSessionResult?> AcceptInviteAsync(AcceptInviteRequest r, CancellationToken c = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Forgot_page_says_unavailable_when_email_is_off()
    {
        var page = Pages.With(new ForgotPasswordModel(new FakeAuth { Enabled = false }, NullLogger<ForgotPasswordModel>.Instance), null);
        await page.OnGetAsync(default);
        Assert.True(page.Unavailable);
    }

    [Fact]
    public async Task Forgot_post_goes_to_step_two_without_the_code_in_the_url()
    {
        var auth = new FakeAuth();
        var page = Pages.With(new ForgotPasswordModel(auth, NullLogger<ForgotPasswordModel>.Instance), null);
        page.Email = " ama@shop.gh ";
        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(default));
        Assert.Equal("/Account/ResetPassword", redirect.PageName);
        Assert.Equal("ama@shop.gh", redirect.RouteValues!["email"]);
        Assert.Equal(2, redirect.RouteValues.Count);
        Assert.Equal("ask:ama@shop.gh", Assert.Single(auth.Calls));
    }

    [Fact]
    public async Task Forgot_post_shows_server_error()
    {
        var auth = new FakeAuth { Result = false, LastErrorMessage = "Too many tries." };
        var page = Pages.With(new ForgotPasswordModel(auth, NullLogger<ForgotPasswordModel>.Instance), null);
        page.Email = "ama@shop.gh";
        Assert.IsType<PageResult>(await page.OnPostAsync(default));
        Assert.Equal("Too many tries.", page.ErrorMessage);
    }

    [Fact]
    public async Task Reset_success_sends_to_sign_in_with_a_notice_and_error_stays_on_page()
    {
        var auth = new FakeAuth();
        var page = Pages.With(new ResetPasswordModel(auth, NullLogger<ResetPasswordModel>.Instance), null);
        page.Input = new ResetPasswordModel.ResetInput { Email = "ama@shop.gh", Code = " 1234 5678 ", NewPassword = "fresh123", ConfirmPassword = "fresh123" };
        var ok = Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(default));
        Assert.Equal("/Account/Login", ok.PageName);
        Assert.NotNull(page.Notice);
        Assert.Equal("reset:ama@shop.gh:1234 5678", Assert.Single(auth.Calls));

        auth.Result = false;
        auth.LastErrorMessage = "That code isn't right or has expired.";
        Assert.IsType<PageResult>(await page.OnPostAsync(default));
        Assert.Equal("That code isn't right or has expired.", page.ErrorMessage);
    }

    [Fact]
    public async Task Owner_moves_orders_and_sees_who_got_them()
    {
        var api = new FakeApi { Reassign = (from, to) => ApiResult<ReassignOrdersResultDto>.Ok(new ReassignOrdersResultDto(3, to, "Esi Mensah")) };
        var page = Pages.With(new StaffIndex(api), BusinessRole.Owner);
        Assert.IsType<RedirectToPageResult>(await page.OnPostReassignAsync("u2", "u3", default));
        Assert.Contains("reassign:u2:u3", api.Calls);
        Assert.Contains("3 open orders moved to Esi Mensah", page.Flash);

        api.Reassign = (_, _) => ApiResult<ReassignOrdersResultDto>.Ok(new ReassignOrdersResultDto(1, null));
        await page.OnPostReassignAsync("u2", null, default);
        Assert.Contains("1 open order now unassigned", page.Flash);
    }

    [Theory]
    [InlineData(BusinessRole.Manager)]
    [InlineData(BusinessRole.Tailor)]
    public async Task Only_the_owner_can_move_orders(string role)
    {
        var api = new FakeApi();
        var page = Pages.With(new StaffIndex(api), role);
        await page.OnPostReassignAsync("u2", "u3", default);
        Assert.DoesNotContain(api.Calls, c => c.StartsWith("reassign"));
    }

    [Fact]
    public async Task Reassign_targets_are_other_active_members()
    {
        var now = Data.Modified;
        var from = new StaffMemberDto("u2", "Kofi", "k@shop.gh", BusinessRole.Tailor, false, now, 2, false);
        var api = new FakeApi
        {
            StaffOverview = () => ApiResult<StaffOverviewDto>.Ok(new StaffOverviewDto(
            [
                new StaffMemberDto("u1", "Ama", "a@shop.gh", BusinessRole.Owner, true, now, 0, true),
                from,
                new StaffMemberDto("u3", "Esi", "e@shop.gh", BusinessRole.Manager, true, now, 0, false),
                new StaffMemberDto("u4", "Yaw", "y@shop.gh", BusinessRole.Tailor, false, now, 0, false)
            ], [], true))
        };
        var page = Pages.With(new StaffIndex(api), BusinessRole.Owner);
        await page.OnGetAsync(default);
        Assert.Equal(["u1", "u3"], page.ReassignTargets(from).Select(m => m.UserId));
    }
}
