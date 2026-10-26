using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

public class StaffMobileTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static StaffMemberDto Member(string id, string role, bool you = false, bool active = true, int open = 0) =>
        new(id, "Name " + id, id + "@shop.gh", role, active, Now, open, you);

    private sealed class FakeStaffApi : IStaffApi
    {
        public List<string> Calls { get; } = new();
        public StaffOverviewDto Overview { get; set; } = new([Member("o", BusinessRole.Owner, you: true)], [], true);
        public StaffCallResult<CreatedInvitationDto>? InviteResult { get; set; }

        public Task<StaffCallResult<StaffOverviewDto>> GetAsync(CancellationToken c = default)
        {
            Calls.Add("get");
            return Task.FromResult(StaffCallResult<StaffOverviewDto>.Ok(Overview));
        }

        public Task<StaffCallResult<CreatedInvitationDto>> InviteAsync(CreateInvitationRequest r, CancellationToken c = default)
        {
            Calls.Add($"invite:{r.Email}:{r.Role}:{r.Name}");
            return Task.FromResult(InviteResult ?? StaffCallResult<CreatedInvitationDto>.Ok(
                new CreatedInvitationDto(new InvitationDto("i1", r.Email, r.Name, r.Role, "Pending", Now, Now.AddDays(7), null), "ABCD-EFGH-JKMN")));
        }

        public Task<StaffCallResult<bool>> RevokeAsync(string id, CancellationToken c = default)
        {
            Calls.Add("revoke:" + id);
            return Task.FromResult(StaffCallResult<bool>.Ok(true));
        }

        public Task<StaffCallResult<StaffMemberDto>> ChangeRoleAsync(string userId, string role, CancellationToken c = default)
        {
            Calls.Add($"role:{userId}:{role}");
            return Task.FromResult(StaffCallResult<StaffMemberDto>.Ok(null));
        }

        public Task<StaffCallResult<StaffMemberDto>> SetActiveAsync(string userId, bool active, CancellationToken c = default)
        {
            Calls.Add($"active:{userId}:{active}");
            return Task.FromResult(StaffCallResult<StaffMemberDto>.Ok(null));
        }

        public Task<StaffCallResult<ReassignOrdersResultDto>> ReassignOrdersAsync(string fromUserId, string? toUserId, CancellationToken c = default)
        {
            Calls.Add($"move:{fromUserId}:{toUserId ?? "none"}");
            return Task.FromResult(StaffCallResult<ReassignOrdersResultDto>.Ok(new ReassignOrdersResultDto(2, toUserId, toUserId is null ? null : "Name " + toUserId)));
        }
    }

    [Fact]
    public async Task Solo_owner_sees_invite_prompt()
    {
        var vm = new StaffViewModel(new FakeStaffApi());
        await vm.LoadAsync();
        Assert.True(vm.Loaded && vm.IsSolo && vm.CanManage);
        Assert.False(Assert.Single(vm.Members).CanChange);
    }

    [Fact]
    public async Task Invite_validates_email_then_shows_code_once_and_clears_form()
    {
        var api = new FakeStaffApi();
        var vm = new StaffViewModel(api);
        await vm.LoadAsync();
        vm.InviteEmail = "not an email";
        await vm.SendInviteCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.DoesNotContain(api.Calls, c => c.StartsWith("invite"));

        vm.InviteEmail = " ama@shop.gh ";
        vm.InviteName = "  ";
        await vm.SendInviteCommand.ExecuteAsync(null);
        Assert.Contains("invite:ama@shop.gh:Tailor:", api.Calls);
        Assert.Equal("ABCD-EFGH-JKMN", vm.NewCode);
        Assert.Equal(string.Empty, vm.InviteEmail);
        Assert.Contains("ABCD-EFGH-JKMN", vm.ShareMessage("Ama Stitches"));
        vm.DismissCodeCommand.Execute(null);
        Assert.False(vm.HasNewCode);
    }

    [Fact]
    public async Task Invite_error_keeps_form_and_shows_message()
    {
        var api = new FakeStaffApi { InviteResult = new StaffCallResult<CreatedInvitationDto>(null, "That email already has a NeedleBase account.") };
        var vm = new StaffViewModel(api);
        await vm.LoadAsync();
        vm.InviteEmail = "ama@shop.gh";
        await vm.SendInviteCommand.ExecuteAsync(null);
        Assert.Equal("That email already has a NeedleBase account.", vm.ErrorMessage);
        Assert.Equal("ama@shop.gh", vm.InviteEmail);
        Assert.False(vm.HasNewCode);
    }

    [Fact]
    public async Task Manager_view_has_no_actions()
    {
        var api = new FakeStaffApi { Overview = new([Member("o", BusinessRole.Owner), Member("m", BusinessRole.Manager, you: true), Member("t", BusinessRole.Tailor)], [], false) };
        var vm = new StaffViewModel(api);
        await vm.LoadAsync();
        Assert.All(vm.Members, r => Assert.False(r.CanChange));
        vm.InviteEmail = "x@y.gh";
        await vm.SendInviteCommand.ExecuteAsync(null);
        Assert.DoesNotContain(api.Calls, c => c.StartsWith("invite"));
    }

    [Fact]
    public async Task Owner_turns_off_access_only_after_confirm()
    {
        var api = new FakeStaffApi { Overview = new([Member("o", BusinessRole.Owner, you: true), Member("t", BusinessRole.Tailor, open: 2)], [], true) };
        var confirms = new List<string>();
        var vm = new StaffViewModel(api)
        {
            ChooseAction = (_, actions) => Task.FromResult<string?>(StaffViewModel.ActionTurnOff),
            Confirm = (_, message) => { confirms.Add(message); return Task.FromResult(false); }
        };
        await vm.LoadAsync();
        var tailor = vm.Members.Single(m => m.Member.UserId == "t");
        await vm.ManageCommand.ExecuteAsync(tailor);
        Assert.DoesNotContain("active:t:False", api.Calls);
        Assert.Contains("2 open order", Assert.Single(confirms));

        vm.Confirm = (_, _) => Task.FromResult(true);
        await vm.ManageCommand.ExecuteAsync(tailor);
        Assert.Contains("active:t:False", api.Calls);
        Assert.True(vm.HasStatus);
    }

    [Fact]
    public async Task Owner_promotes_tailor_and_pending_invite_can_be_cancelled()
    {
        var invite = new InvitationDto("i9", "kofi@shop.gh", null, BusinessRole.Tailor, "Pending", Now, Now.AddDays(7), null);
        var accepted = invite with { Id = "i8", Status = "Accepted" };
        var api = new FakeStaffApi { Overview = new([Member("o", BusinessRole.Owner, you: true), Member("t", BusinessRole.Tailor)], [invite, accepted], true) };
        var vm = new StaffViewModel(api) { ChooseAction = (_, _) => Task.FromResult<string?>(StaffViewModel.ActionMakeManager) };
        await vm.LoadAsync();
        Assert.Single(vm.Invites); // accepted invites show as members instead
        await vm.ManageCommand.ExecuteAsync(vm.Members.Single(m => m.Member.UserId == "t"));
        Assert.Contains("role:t:Manager", api.Calls);
        await vm.RevokeCommand.ExecuteAsync(vm.Invites[0]);
        Assert.Contains("revoke:i9", api.Calls);
    }

    [Theory]
    [InlineData("ama@shop.gh", true)]
    [InlineData("Ama <ama@shop.gh>", false)]
    [InlineData("ama@shop", false)]
    [InlineData("a b@shop.gh", false)]
    public void Email_check(string value, bool ok) => Assert.Equal(ok, StaffViewModel.IsEmail(value));

    [Theory]
    [InlineData("abc123", true)]
    [InlineData("ABC123", false)]
    [InlineData("abcdef", false)]
    [InlineData("ab1", false)]
    public void Password_rule_matches_server(string value, bool ok) => Assert.Equal(ok, PasswordRules.IsStrongEnough(value));

    private sealed class PwAuth : IAuthService
    {
        public bool Result { get; set; } = true;
        public int Calls { get; private set; }
        public string? LastErrorMessage { get; set; }
        public Task<bool> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }

        public List<string> ResetCalls { get; } = new();
        public Task<bool> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
        {
            ResetCalls.Add("ask:" + email);
            return Task.FromResult(Result);
        }

        public Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
        {
            ResetCalls.Add($"reset:{request.Email}:{request.Code}");
            return Task.FromResult(Result);
        }

        public Task<AuthUserDto?> RegisterBusinessAsync(RegisterBusinessRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthUserDto?>(null);
        public Task<AuthUserDto?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthUserDto?>(null);
        public Task<TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasStoredTokensAsync() => Task.FromResult(true);
        public Task ClearTokensAsync() => Task.CompletedTask;
        public Task<bool> LogoutAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("t");
        public Task StoreTokensAsync(TokenResponse tokens) => Task.CompletedTask;
        public Task StoreSessionAsync(AuthUserDto user) => Task.CompletedTask;
        public Task<string?> GetBusinessIdAsync() => Task.FromResult<string?>("b");
        public Task<AuthUserDto?> GetCachedUserAsync() => Task.FromResult<AuthUserDto?>(null);
    }

    [Fact]
    public async Task Change_password_checks_locally_then_clears_on_success()
    {
        var auth = new PwAuth();
        var vm = new ChangePasswordViewModel(auth) { CurrentPassword = "old123", NewPassword = "new123", ConfirmPassword = "new124" };
        await vm.SaveAsync();
        Assert.Equal(0, auth.Calls);
        Assert.Contains("match", vm.ErrorMessage);

        vm.ConfirmPassword = "new123";
        await vm.SaveAsync();
        Assert.Equal(1, auth.Calls);
        Assert.True(vm.HasStatus);
        Assert.Equal(string.Empty, vm.CurrentPassword);
        Assert.Equal(string.Empty, vm.NewPassword);
    }

    [Fact]
    public async Task Change_password_shows_server_error()
    {
        var auth = new PwAuth { Result = false, LastErrorMessage = "Your current password isn't right." };
        var vm = new ChangePasswordViewModel(auth) { CurrentPassword = "bad123", NewPassword = "new123", ConfirmPassword = "new123" };
        await vm.SaveAsync();
        Assert.Equal("Your current password isn't right.", vm.ErrorMessage);
        Assert.False(vm.HasStatus);
    }

    [Fact]
    public async Task Turning_off_someone_with_open_orders_offers_to_move_them()
    {
        var api = new FakeStaffApi { Overview = new([Member("o", BusinessRole.Owner, you: true), Member("t", BusinessRole.Tailor, open: 2), Member("m", BusinessRole.Manager), Member("x", BusinessRole.Tailor, active: false)], [], true) };
        string[]? offered = null;
        var vm = new StaffViewModel(api)
        {
            Confirm = (_, _) => Task.FromResult(true),
            ChooseAction = (title, actions) =>
            {
                if (title.StartsWith("Move", StringComparison.Ordinal))
                {
                    offered = actions;
                    return Task.FromResult<string?>("Name m · Manager");
                }

                return Task.FromResult<string?>(StaffViewModel.ActionTurnOff);
            }
        };
        await vm.LoadAsync();
        await vm.ManageCommand.ExecuteAsync(vm.Members.Single(m => m.Member.UserId == "t"));
        Assert.Contains("active:t:False", api.Calls);
        Assert.Contains("move:t:m", api.Calls);
        // Only active others (owner included), never the person themselves or turned-off members.
        Assert.Equal(["Name o · Owner", "Name m · Manager", StaffViewModel.LeaveUnassigned], offered!);
        Assert.Contains("Moved 2 open orders", vm.StatusMessage);
    }

    [Fact]
    public async Task Turned_off_member_orders_can_be_left_unassigned_later()
    {
        var api = new FakeStaffApi { Overview = new([Member("o", BusinessRole.Owner, you: true), Member("x", BusinessRole.Tailor, active: false, open: 2)], [], true) };
        var vm = new StaffViewModel(api)
        {
            ChooseAction = (title, actions) => Task.FromResult<string?>(title.StartsWith("Move", StringComparison.Ordinal) ? StaffViewModel.LeaveUnassigned : StaffViewModel.ActionMoveOrders)
        };
        await vm.LoadAsync();
        await vm.ManageCommand.ExecuteAsync(vm.Members.Single(m => m.Member.UserId == "x"));
        Assert.Contains("move:x:none", api.Calls);
        Assert.Contains("unassigned", vm.StatusMessage);
        Assert.DoesNotContain(api.Calls, c => c.StartsWith("active:"));
    }

    [Fact]
    public async Task Cancelling_the_move_choice_moves_nothing()
    {
        var api = new FakeStaffApi { Overview = new([Member("o", BusinessRole.Owner, you: true), Member("x", BusinessRole.Tailor, active: false, open: 1)], [], true) };
        var vm = new StaffViewModel(api)
        {
            ChooseAction = (title, _) => Task.FromResult<string?>(title.StartsWith("Move", StringComparison.Ordinal) ? null : StaffViewModel.ActionMoveOrders)
        };
        await vm.LoadAsync();
        await vm.ManageCommand.ExecuteAsync(vm.Members.Single(m => m.Member.UserId == "x"));
        Assert.DoesNotContain(api.Calls, c => c.StartsWith("move"));
    }

    [Fact]
    public async Task Emailed_invite_says_so()
    {
        var api = new FakeStaffApi
        {
            InviteResult = StaffCallResult<CreatedInvitationDto>.Ok(new CreatedInvitationDto(
                new InvitationDto("i1", "ama@shop.gh", null, BusinessRole.Tailor, "Pending", Now, Now.AddDays(7), null), "ABCD-EFGH-JKMN", Emailed: true))
        };
        var vm = new StaffViewModel(api);
        await vm.LoadAsync();
        vm.InviteEmail = "ama@shop.gh";
        await vm.SendInviteCommand.ExecuteAsync(null);
        Assert.Contains("emailing", vm.NewCodeNote);
        Assert.Equal("ABCD-EFGH-JKMN", vm.NewCode); // still shown for WhatsApp/SMS
    }

    [Fact]
    public async Task Forgot_password_two_steps_validates_locally_and_clears_secrets()
    {
        var auth = new PwAuth();
        var vm = new ForgotPasswordViewModel(auth) { Email = "nope" };
        await vm.SendCodeAsync();
        Assert.Empty(auth.ResetCalls);
        Assert.True(vm.NeedsEmail);

        vm.Email = " ama@shop.gh ";
        await vm.SendCodeAsync();
        Assert.Equal("ask:ama@shop.gh", Assert.Single(auth.ResetCalls));
        Assert.True(vm.CanEnterCode);
        Assert.Contains("If ama@shop.gh has", vm.StatusMessage); // never says whether the account exists

        vm.Code = "1234 567";
        vm.NewPassword = vm.ConfirmPassword = "fresh123";
        await vm.ResetAsync();
        Assert.Contains("8-digit", vm.ErrorMessage);

        vm.Code = "1234 5678";
        vm.NewPassword = "FRESH123";
        await vm.ResetAsync();
        Assert.Single(auth.ResetCalls);

        vm.NewPassword = vm.ConfirmPassword = "fresh123";
        await vm.ResetAsync();
        Assert.Equal("reset:ama@shop.gh:12345678", auth.ResetCalls[1]);
        Assert.True(vm.Done);
        Assert.Equal(string.Empty, vm.Code);
        Assert.Equal(string.Empty, vm.NewPassword);
    }

    [Fact]
    public async Task Forgot_password_shows_server_error_and_keeps_the_code_step()
    {
        var auth = new PwAuth();
        var vm = new ForgotPasswordViewModel(auth) { Email = "ama@shop.gh" };
        await vm.SendCodeAsync();
        auth.Result = false;
        auth.LastErrorMessage = "That code isn't right or has expired.";
        vm.Code = "12345678";
        vm.NewPassword = vm.ConfirmPassword = "fresh123";
        await vm.ResetAsync();
        Assert.Equal("That code isn't right or has expired.", vm.ErrorMessage);
        Assert.True(vm.CanEnterCode);
        Assert.False(vm.Done);
    }

    [Fact]
    public void Password_hint_comes_from_the_shared_rule() =>
        Assert.Equal(NeedleBase.Application.Common.PasswordPolicy.Hint, PasswordRules.Hint);
}
