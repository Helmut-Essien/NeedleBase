using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Domain.Tests;

public class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static Invitation New(string role = BusinessRole.Tailor) =>
        Invitation.Create("i1", "b1", "  ama@shop.gh ", " Ama ", role, "HASH", "owner", Now);

    [Fact]
    public void Create_trims_and_expires_in_seven_days()
    {
        var i = New();
        Assert.Equal("ama@shop.gh", i.Email);
        Assert.Equal("Ama", i.Name);
        Assert.Equal(Now.AddDays(7), i.ExpiresAtUtc);
        Assert.Equal(InvitationStatus.Pending, i.StatusAt(Now));
    }

    [Theory]
    [InlineData(BusinessRole.Owner)]
    [InlineData("Boss")]
    public void Owner_or_unknown_role_is_refused(string role) =>
        Assert.Throws<ArgumentException>(() => New(role));

    [Fact]
    public void Bad_email_is_refused() =>
        Assert.Throws<ArgumentException>(() => Invitation.Create("i1", "b1", "not-an-email", null, BusinessRole.Tailor, "H", "o", Now));

    [Fact]
    public void Expires_exactly_at_lifetime()
    {
        var i = New();
        Assert.True(i.IsPendingAt(Now.AddDays(7).AddTicks(-1)));
        Assert.Equal(InvitationStatus.Expired, i.StatusAt(Now.AddDays(7)));
        Assert.Throws<InvalidOperationException>(() => i.Accept("u1", Now.AddDays(7)));
    }

    [Fact]
    public void Accept_once_then_used()
    {
        var i = New();
        i.Accept("u1", Now);
        Assert.Equal(InvitationStatus.Accepted, i.StatusAt(Now.AddDays(30)));
        Assert.Throws<InvalidOperationException>(() => i.Accept("u2", Now));
        Assert.Throws<InvalidOperationException>(() => i.Revoke(Now));
    }

    [Fact]
    public void Revoke_is_idempotent_and_blocks_accept()
    {
        var i = New();
        i.Revoke(Now);
        i.Revoke(Now.AddHours(1));
        Assert.Equal(Now, i.RevokedAtUtc);
        Assert.Equal(InvitationStatus.Revoked, i.StatusAt(Now));
        Assert.Throws<InvalidOperationException>(() => i.Accept("u1", Now));
    }
}

public class StaffMembershipRoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Owner_cannot_be_changed_or_deactivated()
    {
        var owner = StaffMembership.Create("m1", "b1", "u1", BusinessRole.Owner, Now);
        Assert.Throws<InvalidOperationException>(() => owner.ChangeRole(BusinessRole.Manager));
        Assert.Throws<InvalidOperationException>(() => owner.Deactivate());
        Assert.True(owner.IsActive);
    }

    [Fact]
    public void Staff_role_and_access_change()
    {
        var m = StaffMembership.Create("m2", "b1", "u2", BusinessRole.Tailor, Now);
        m.ChangeRole(BusinessRole.Manager);
        Assert.Equal(BusinessRole.Manager, m.Role);
        Assert.Throws<ArgumentException>(() => m.ChangeRole(BusinessRole.Owner));
        m.Deactivate();
        Assert.False(m.IsActive);
        m.Reactivate();
        Assert.True(m.IsActive);
    }
}
