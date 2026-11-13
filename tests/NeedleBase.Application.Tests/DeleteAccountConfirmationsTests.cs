using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Application.Tests;

public class DeleteAccountConfirmationsTests
{
    [Theory]
    [InlineData("DELETE")]
    [InlineData("delete")]
    [InlineData(" Delete ")]
    [InlineData("\tDELETE\n")]
    public void Staff_Phrase_AllowsCaseAndSurroundingWhitespace(string actual) =>
        Assert.True(DeleteAccountConfirmations.Matches(actual, DeleteAccountConfirmations.Staff));

    [Theory]
    [InlineData("DELETE SHOP")]
    [InlineData("delete shop")]
    [InlineData("  delete   shop\t")]
    [InlineData("DELETE\u00A0SHOP")]
    public void Owner_Phrase_CollapsesWhitespace(string actual) =>
        Assert.True(DeleteAccountConfirmations.Matches(actual, DeleteAccountConfirmations.OwnerShop));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("DELET")]
    [InlineData("DELETE SHOP NOW")]
    [InlineData("DELETE")]
    public void Owner_Phrase_RejectsOtherText(string? actual) =>
        Assert.False(DeleteAccountConfirmations.Matches(actual, DeleteAccountConfirmations.OwnerShop));
}
