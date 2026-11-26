using NeedleBase.Domain.Common;

namespace NeedleBase.Domain.Tests;

public class EmailAddressesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CheckOptional_Empty_Ok(string? email) =>
        Assert.Null(EmailAddresses.CheckOptional(email));

    [Theory]
    [InlineData("a@b.c")]
    [InlineData("owner@ama-stitches.gh")]
    public void CheckOptional_Valid(string email) =>
        Assert.Null(EmailAddresses.CheckOptional(email));

    [Theory]
    [InlineData("no-at")]
    [InlineData("@nodomain")]
    [InlineData("local@")]
    [InlineData("a @b.c")]
    [InlineData("a@@b.c")]
    public void CheckOptional_Invalid(string email) =>
        Assert.NotNull(EmailAddresses.CheckOptional(email));
}
