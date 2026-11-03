using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Application.Tests;

public sealed class LicensingRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static Business Shop(LicenseCheck? check = null, DateTimeOffset? at = null)
    {
        var b = Business.Create("01JBUSINESSRULES00000000001", "Kofi Tailoring", null, Now.AddDays(-60));
        if (check is not null)
        {
            b.AttachLicense("protected", "…WXYZ", check, at ?? Now);
        }

        return b;
    }

    [Fact]
    public void No_key_is_not_attached_and_never_read_only()
    {
        var s = SubscriptionRules.Compute(Shop(), Now, 7);
        Assert.Equal(SubscriptionStates.NotAttached, s.State);
        Assert.True(s.OwnerOnly);
        Assert.False(s.IsReadOnly);
    }

    [Fact]
    public void Unreadable_key_needs_reattach()
    {
        var s = SubscriptionRules.Compute(Shop(new LicenseCheck(true, "valid")), Now, 7, keyReadable: false);
        Assert.Equal(SubscriptionStates.NeedsReattach, s.State);
        Assert.False(s.IsReadOnly);
    }

    [Theory]
    [InlineData(false, false, "active")]
    [InlineData(true, false, "trial")]
    [InlineData(false, true, "complimentary")]
    public void Valid_states(bool trial, bool comp, string expected)
    {
        var s = SubscriptionRules.Compute(Shop(new LicenseCheck(true, "valid", IsTrial: trial, IsComplimentary: comp)), Now, 7);
        Assert.Equal(expected, s.State);
        Assert.Null(s.Message);
    }

    [Fact]
    public void Expiry_within_warning_window_warns_but_complimentary_does_not()
    {
        var soon = Shop(new LicenseCheck(true, "valid", ExpiresAtUtc: Now.AddDays(5)));
        Assert.Contains("8 Nov 2026", SubscriptionRules.Compute(soon, Now, 7).Message);

        var comp = Shop(new LicenseCheck(true, "valid", IsComplimentary: true, ExpiresAtUtc: Now.AddDays(5)));
        Assert.Null(SubscriptionRules.Compute(comp, Now, 7).Message);
    }

    [Fact]
    public void Not_valid_is_a_problem_until_grace_then_read_only()
    {
        var b = Shop(new LicenseCheck(true, "valid", GraceUntilUtc: Now.AddDays(10)), Now.AddDays(-1));
        b.RecordLicenseCheck(new LicenseCheck(false, "expired"), Now);

        // Token grace (10 days) outlasts our 7 days.
        Assert.Equal(Now.AddDays(10), SubscriptionRules.ReadOnlyFrom(b, 7));
        var problem = SubscriptionRules.Compute(b, Now.AddDays(9), 7);
        Assert.Equal(SubscriptionStates.Problem, problem.State);
        Assert.False(problem.IsReadOnly);

        var locked = SubscriptionRules.Compute(b, Now.AddDays(10), 7);
        Assert.Equal(SubscriptionStates.ReadOnly, locked.State);
        Assert.True(locked.IsReadOnly);
        Assert.Contains("has expired", locked.Message);
    }

    [Fact]
    public void Negative_grace_days_count_as_zero()
    {
        var b = Shop(new LicenseCheck(true, "valid"), Now.AddDays(-1));
        b.RecordLicenseCheck(new LicenseCheck(false, "revoked"), Now);
        Assert.Equal(Now, SubscriptionRules.ReadOnlyFrom(b, -3));
    }

    [Theory]
    [InlineData("NB-1234-ABCD", "…ABCD")]
    [InlineData("  ab  ", "…")]
    public void KeyHint_shows_at_most_last_four(string key, string expected) =>
        Assert.Equal(expected, SubscriptionRules.KeyHint(key));

    [Theory]
    [InlineData("invalid_key", LicenseErrors.KeyInvalid)]
    [InlineData("device_limit", LicenseErrors.KeyInUse)]
    [InlineData("expired", LicenseErrors.Expired)]
    [InlineData("suspended", LicenseErrors.Suspended)]
    [InlineData("revoked", LicenseErrors.Revoked)]
    [InlineData("not_active", LicenseErrors.NotActive)]
    [InlineData("something_new", LicenseErrors.CheckUnavailable)]
    [InlineData(null, LicenseErrors.CheckUnavailable)]
    public void ForReason_maps_each_reason(string? reason, string expected) =>
        Assert.Equal(expected, LicenseErrors.ForReason(reason));

    [Fact]
    public void Decide_allows_only_valid_with_matching_email()
    {
        var ok = new LicenseCheck(true, "valid");
        Assert.Null(LicenseErrors.Decide(LicensePlatformOutcome.Judged(ok, true)));
        Assert.Equal(LicenseErrors.EmailMismatch, LicenseErrors.Decide(LicensePlatformOutcome.Judged(ok, false)));
        Assert.Equal(LicenseErrors.EmailMismatch, LicenseErrors.Decide(LicensePlatformOutcome.Judged(ok)));
        Assert.Equal(LicenseErrors.Expired, LicenseErrors.Decide(LicensePlatformOutcome.Judged(new LicenseCheck(false, "expired"), true)));
        Assert.Equal(LicenseErrors.CheckUnavailable, LicenseErrors.Decide(LicensePlatformOutcome.NotAvailable("timeout")));
    }

    [Fact]
    public void Every_code_has_a_message()
    {
        foreach (var code in new[] { LicenseErrors.KeyRequired, LicenseErrors.KeyInvalid, LicenseErrors.KeyInUse, LicenseErrors.EmailMismatch,
                     LicenseErrors.Expired, LicenseErrors.Suspended, LicenseErrors.Revoked, LicenseErrors.NotActive,
                     LicenseErrors.CheckUnavailable, LicenseErrors.NotConfigured, LicenseErrors.ReadOnly })
        {
            Assert.False(string.IsNullOrWhiteSpace(LicenseErrors.MessageFor(code)), code);
        }
    }
}
