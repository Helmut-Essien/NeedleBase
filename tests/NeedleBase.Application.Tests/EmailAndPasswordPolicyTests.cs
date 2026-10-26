using System.Text.RegularExpressions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Email;

namespace NeedleBase.Application.Tests;

public class EmailAndPasswordPolicyTests
{
    private static readonly DateTimeOffset Expires = new(2026, 11, 2, 14, 5, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("abc123", true)]
    [InlineData("ABC123", false)]
    [InlineData("abcdef", false)]
    [InlineData("ab1", false)]
    [InlineData("ábc１２３", false)] // non-ASCII letters/digits don't count (same as the server's Identity rules)
    [InlineData(null, false)]
    public void Policy_matches_server_rules(string? value, bool ok)
    {
        Assert.Equal(ok, PasswordPolicy.IsStrongEnough(value));
        if (value is not null && value.Length >= PasswordPolicy.MinLength)
        {
            Assert.Equal(ok, Regex.IsMatch(value, PasswordPolicy.Pattern));
        }
    }

    [Fact]
    public void Invite_email_has_code_encoded_names_gmt_time_and_link()
    {
        var m = EmailTemplates.Invite("esi@shop.gh", "Esi <script>", "Ama & Sons", "Tailor", "ABCD-EFGH-JKMN", Expires, "https://app.needlebase.test/");
        Assert.Contains("ABCD-EFGH-JKMN", m.TextBody);
        Assert.Contains("ABCD-EFGH-JKMN", m.HtmlBody);
        Assert.Contains("Mon 2 Nov 2026, 14:05 GMT", m.TextBody);
        Assert.Contains("Ama &amp; Sons", m.HtmlBody);
        Assert.DoesNotContain("<script>", m.HtmlBody);
        Assert.Contains("https://app.needlebase.test/Account/AcceptInvite", m.TextBody);
        Assert.DoesNotContain("ABCD-EFGH-JKMN", m.TextBody[m.TextBody.IndexOf("https://", StringComparison.Ordinal)..].Split('\n')[0]); // code never in a link
        Assert.Equal("invite", m.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public void No_link_without_a_safe_base_url(string? baseUrl)
    {
        var m = EmailTemplates.PasswordReset("ama@shop.gh", null, "1234 5678", TimeSpan.FromMinutes(30), baseUrl);
        Assert.DoesNotContain("href", m.HtmlBody);
        Assert.DoesNotContain("javascript", m.TextBody);
        Assert.Contains("1234 5678", m.TextBody);
        Assert.Contains("30 minutes", m.TextBody);
        Assert.Equal("password_reset", m.Kind);
    }
}

public sealed class EmailHeaderSafetyTests
{
    [Fact]
    public void NamesWithLineBreaks_AreFlattened_InSubjectAndTo()
    {
        var m = NeedleBase.Application.Email.EmailTemplates.Invite("k@example.test", "Kofi\r\nBcc: x@example.test",
            "Ama\nStitches\t", "Tailor", "ABCD 1234", DateTimeOffset.UnixEpoch, null);
        Assert.DoesNotContain('\n', m.Subject);
        Assert.DoesNotContain('\r', m.Subject);
        Assert.Equal("Join Ama Stitches on NeedleBase", m.Subject);
        Assert.Equal("Kofi Bcc: x@example.test", m.ToName);
        Assert.Null(NeedleBase.Application.Email.EmailTemplates.OneLine(null));
    }
}
