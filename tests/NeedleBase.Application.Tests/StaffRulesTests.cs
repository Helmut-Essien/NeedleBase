using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Orders;
using NeedleBase.Application.Staff;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Tests;

public class InviteCodesTests
{
    [Fact]
    public void New_codes_are_formatted_and_unambiguous()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var code = InviteCodes.NewCode();
            Assert.Matches("^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$", code);
            Assert.True(seen.Add(code));
        }
    }

    [Theory]
    [InlineData("abcd-efgh-jkmn", "ABCDEFGHJKMN")]
    [InlineData(" ABCD EFGH JKMN ", "ABCDEFGHJKMN")]
    [InlineData("ABCDEFGHJKMN", "ABCDEFGHJKMN")]
    public void Normalize_accepts_typing_variations(string input, string expected) =>
        Assert.Equal(expected, InviteCodes.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCD-EFGH-JKM")]
    [InlineData("ABCD-EFGH-JKMNP")]
    [InlineData("ABCD-EFGH-JKM0")]
    [InlineData("ABCD-EFGH-JKMO")]
    public void Normalize_rejects_bad_codes(string? input) => Assert.Null(InviteCodes.Normalize(input));

    [Fact]
    public void Hash_is_stable_across_formatting_and_hides_the_code()
    {
        var h = InviteCodes.Hash("abcd-efgh-jkmn");
        Assert.Equal(h, InviteCodes.Hash("ABCDEFGHJKMN"));
        Assert.Equal(64, h!.Length);
        Assert.DoesNotContain("ABCD", h);
        Assert.Null(InviteCodes.Hash("bad"));
    }
}

public class StaffRolesTests
{
    [Fact]
    public void Only_owner_manages_owner_and_manager_view()
    {
        Assert.True(StaffRoles.CanManage(BusinessRole.Owner));
        Assert.False(StaffRoles.CanManage(BusinessRole.Manager));
        Assert.True(StaffRoles.CanView(BusinessRole.Manager));
        Assert.False(StaffRoles.CanView(BusinessRole.Tailor));
        Assert.False(StaffRoles.CanView(null));
        Assert.False(StaffRoles.IsAssignable(BusinessRole.Owner));
        Assert.All(StaffRoles.Assignable, r => Assert.False(string.IsNullOrWhiteSpace(StaffRoles.Summary(r))));
    }

    [Theory]
    [InlineData(BusinessRole.Owner, "u1", "u2", true)]
    [InlineData(BusinessRole.Manager, "u1", "u2", true)]
    [InlineData(BusinessRole.Tailor, "u1", "u1", true)]
    [InlineData(BusinessRole.Tailor, "u1", null, true)]
    [InlineData(BusinessRole.Tailor, "u1", "u2", false)]
    public void Tailor_moves_only_own_or_unassigned(string role, string user, string? assignee, bool expected) =>
        Assert.Equal(expected, OrderEditor.CanChangeStatus(role, user, assignee));

    [Theory]
    [InlineData(StaffErrorMessages.InviteExpired)]
    [InlineData(StaffErrorMessages.InviteUsed)]
    [InlineData(StaffErrorMessages.PasswordIncorrect)]
    [InlineData(StaffErrorMessages.StaffOwner)]
    public void Every_code_has_friendly_copy(string code)
    {
        var text = StaffErrorMessages.For(code);
        Assert.NotEqual(StaffErrorMessages.Fallback, text);
        Assert.DoesNotContain("_", text);
    }

    [Fact]
    public void Problem_mapping_handles_throttle_and_server_errors()
    {
        var none = new Dictionary<string, string[]>();
        Assert.Contains("Too many", StaffErrorMessages.ForProblem(new ApiProblem(429, "LOGIN_THROTTLED", null, null, none)));
        Assert.Equal(AuthErrorMessages.ServerError, StaffErrorMessages.ForProblem(new ApiProblem(500, null, null, null, none)));
        Assert.Equal(StaffErrorMessages.For(StaffErrorMessages.InviteRevoked), StaffErrorMessages.ForProblem(new ApiProblem(410, StaffErrorMessages.InviteRevoked, null, null, none)));
    }
}
