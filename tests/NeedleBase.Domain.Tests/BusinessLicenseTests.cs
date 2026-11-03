using NeedleBase.Domain.Entities;

namespace NeedleBase.Domain.Tests;

public sealed class BusinessLicenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static Business Shop() => Business.Create("01JBUSINESSLICENSE000000001", "Adjoa Styles", null, Now);

    [Fact]
    public void AttachLicense_stores_hint_and_valid_claims()
    {
        var b = Shop();
        b.AttachLicense("protected", "…ABCD",
            new LicenseCheck(true, "valid", "lic-1", "pro", ExpiresAtUtc: Now.AddDays(30), RefreshAfterUtc: Now.AddHours(8), Token: "t"), Now);

        Assert.Equal("protected", b.LicenseKeyProtected);
        Assert.Equal("…ABCD", b.LicenseKeyHint);
        Assert.True(b.LicenseIsValid);
        Assert.Equal("pro", b.LicensePlan);
        Assert.Equal(Now, b.LicenseAttachedAtUtc);
        Assert.Equal(Now.AddHours(8), b.LicenseNextCheckUtc);
        Assert.Null(b.LicenseInvalidSinceUtc);
    }

    [Theory]
    [InlineData(1, 30)]       // too soon -> 30 minutes
    [InlineData(60 * 24 * 7, 60 * 24)] // a week -> one day
    public void Valid_check_clamps_the_refresh_hint(int hintMinutes, int expectedMinutes)
    {
        var b = Shop();
        b.RecordLicenseCheck(new LicenseCheck(true, "valid", RefreshAfterUtc: Now.AddMinutes(hintMinutes)), Now);
        Assert.Equal(Now.AddMinutes(expectedMinutes), b.LicenseNextCheckUtc);
    }

    [Fact]
    public void Invalid_since_is_kept_from_the_first_not_valid_answer_and_cleared_by_a_valid_one()
    {
        var b = Shop();
        b.RecordLicenseCheck(new LicenseCheck(false, "expired"), Now);
        b.RecordLicenseCheck(new LicenseCheck(false, "expired"), Now.AddDays(2));
        Assert.Equal(Now, b.LicenseInvalidSinceUtc);
        Assert.Equal(Now.AddDays(2).AddHours(6), b.LicenseNextCheckUtc);
        Assert.False(b.LicenseIsValid);

        b.RecordLicenseCheck(new LicenseCheck(true, "valid"), Now.AddDays(3));
        Assert.Null(b.LicenseInvalidSinceUtc);
        Assert.True(b.LicenseIsValid);
    }

    [Fact]
    public void Unavailable_backs_off_and_never_changes_the_license()
    {
        var b = Shop();
        b.RecordLicenseCheck(new LicenseCheck(true, "valid"), Now);

        b.RecordLicenseCheckUnavailable(Now, null);
        Assert.Equal(Now.AddMinutes(15), b.LicenseNextCheckUtc);
        b.RecordLicenseCheckUnavailable(Now, null);
        Assert.Equal(Now.AddMinutes(30), b.LicenseNextCheckUtc);
        for (var i = 0; i < 30; i++)
        {
            b.RecordLicenseCheckUnavailable(Now, null);
        }

        Assert.Equal(Now.AddHours(6), b.LicenseNextCheckUtc);
        Assert.Equal(20, b.LicenseFailedChecks);
        Assert.True(b.LicenseIsValid);
        Assert.Null(b.LicenseInvalidSinceUtc);
    }

    [Fact]
    public void Unavailable_honours_a_longer_retry_after_but_not_a_huge_one()
    {
        var b = Shop();
        b.RecordLicenseCheckUnavailable(Now, TimeSpan.FromHours(2));
        Assert.Equal(Now.AddHours(2), b.LicenseNextCheckUtc);

        var c = Shop();
        c.RecordLicenseCheckUnavailable(Now, TimeSpan.FromDays(5));
        Assert.Equal(Now.AddMinutes(15), c.LicenseNextCheckUtc);
    }
}
