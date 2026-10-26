using NeedleBase.Application.WhatsApp;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.WhatsApp;
using Xunit;

namespace NeedleBase.Api.Tests;

public class WhatsAppUnitTests
{
    [Theory]
    [InlineData("024 555 0123", "233245550123")]
    [InlineData("+233 (0)24-555-0123", "233245550123")]
    [InlineData("00233245550123", "233245550123")]
    [InlineData("233245550123", "233245550123")]
    [InlineData("+44 7700 900123", "447700900123")]
    [InlineData("0245", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("call me", null)]
    [InlineData("024 555 01234", null)]
    [InlineData("030 221 2345", null)] // Ghana fixed line: no WhatsApp
    [InlineData("+233 30 221 2345", null)]
    [InlineData("055 123 4567", "233551234567")]
    public void Digits_NormaliseGhanaAndInternational(string? phone, string? expected) =>
        Assert.Equal(expected, WhatsAppLinks.ToWhatsAppDigits(phone));

    [Fact]
    public void Message_IsPerStatus_AndUrlIsEncoded()
    {
        var text = WhatsAppLinks.Message(OrderStatus.Ready, "Efua Mensah", "Ama Stitches", "NB-0042");
        Assert.Equal("Hello Efua, this is Ama Stitches. Your order NB-0042 is ready for pickup. You are welcome any time during opening hours. Thank you!", text);
        Assert.Contains("fitting", WhatsAppLinks.Message(OrderStatus.Fitting, null, null, null));
        Assert.StartsWith("Hello, this is your tailor.", WhatsAppLinks.Message(OrderStatus.Cutting, " ", null, null));
        var url = WhatsAppLinks.Url("024 555 0123", "Hi & bye?")!;
        Assert.Equal("https://wa.me/233245550123?text=Hi%20%26%20bye%3F", url);
        Assert.Null(WhatsAppLinks.Url("12", "x"));
        Assert.StartsWith("whatsapp://send?phone=233245550123&text=", WhatsAppLinks.AppUri("0245550123", "x"));
    }

    [Fact]
    public void Settings_KeepOnlyValidStatuses_NeverNew()
    {
        var s = WhatsAppSettings.Create("b1", DateTimeOffset.UtcNow);
        s.Update(true, ["Ready", "New", "Bogus", "Ready", "Fitting"], "u", DateTimeOffset.UtcNow);
        Assert.Equal("Fitting,Ready", s.Statuses);
        Assert.True(s.Sends("Ready"));
        Assert.False(s.Sends("New"));
        s.Update(false, ["Ready"], "u", DateTimeOffset.UtcNow);
        Assert.False(s.Sends("Ready"));
    }

    [Fact]
    public void Message_Backoff_AndDeliveryOnlyMovesForward()
    {
        var now = DateTimeOffset.UtcNow;
        var m = WhatsAppMessage.Create("m1", "b", "o", "c", "Ready", now, TimeSpan.Zero);
        for (var i = 1; i < WhatsAppMessage.MaxAttempts; i++)
        {
            m.Fail("x", permanent: false, now);
            Assert.Equal(WhatsAppMessageState.Pending, m.State);
        }

        m.Fail("x", permanent: false, now);
        Assert.Equal(WhatsAppMessageState.Failed, m.State);

        var d = WhatsAppMessage.Create("m2", "b", "o", "c", "Ready", now, TimeSpan.Zero);
        d.MarkSent("wamid.1", now);
        Assert.True(d.ApplyDelivery(WhatsAppMessageState.Read, null, now));
        Assert.False(d.ApplyDelivery(WhatsAppMessageState.Delivered, null, now));
        Assert.True(d.ApplyDelivery(WhatsAppMessageState.Failed, "131026 undeliverable", now));
        Assert.False(d.ApplyDelivery(WhatsAppMessageState.Read, null, now));
    }

    [Theory]
    [InlineData(200, "{\"messages\":[{\"id\":\"wamid.X\"}]}", true, false)]
    [InlineData(429, "{\"error\":{\"code\":130429,\"message\":\"rate\"}}", false, false)]
    [InlineData(500, "<html>bad gateway</html>", false, false)]
    [InlineData(400, "{\"error\":{\"code\":132001,\"message\":\"Template name does not exist\"}}", false, true)]
    [InlineData(401, "{\"error\":{\"code\":190,\"message\":\"token expired\"}}", false, true)]
    [InlineData(200, "{}", false, false)]
    public void Interpret_ClassifiesMetaReplies(int status, string body, bool ok, bool permanent)
    {
        var r = MetaWhatsAppClient.Interpret((System.Net.HttpStatusCode)status, body);
        Assert.Equal(ok, r.Ok);
        if (!ok)
        {
            Assert.Equal(permanent, r.Permanent);
        }
    }

    [Fact]
    public void Webhook_Signature_AndStopWords()
    {
        var body = "{\"entry\":[]}"u8.ToArray();
        var sig = "sha256=" + Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData("s"u8.ToArray(), body));
        Assert.True(WhatsAppWebhook.SignatureValid("s", body, sig));
        Assert.False(WhatsAppWebhook.SignatureValid("other", body, sig));
        Assert.False(WhatsAppWebhook.SignatureValid("s", body, "sha256=zz"));
        Assert.False(WhatsAppWebhook.SignatureValid(null, body, sig));
        Assert.True(WhatsAppWebhook.IsStop("Stop"));
        Assert.True(WhatsAppWebhook.IsStop(" STOP! "));
        Assert.False(WhatsAppWebhook.IsStop("Don't stop, thanks"));
        Assert.Empty(WhatsAppWebhook.Parse("{\"entry\":[{\"changes\":[{\"value\":{\"statuses\":[{\"id\":\"x\",\"status\":\"weird\"}]}}]}]}"u8).Statuses);
    }

    [Fact]
    public void TemplateParameters_HaveNoNewLines()
    {
        Assert.Equal("Ama Stitches & Co", WhatsAppSender.Param("Ama\n Stitches\t &    Co"));
        Assert.Equal("-", WhatsAppSender.Param("  "));
        Assert.Equal("there", WhatsAppSender.FirstName(null));
    }

    [Fact]
    public void Cuts_NeverSplitAnEmoji()
    {
        var name = new string('a', 59) + "\U0001F600"; // emoji straddles the 60-char limit
        var p = WhatsAppSender.Param(name);
        Assert.Equal(59, p.Length);
        Assert.False(char.IsHighSurrogate(p[^1]));
        Assert.Equal("ab", NeedleBase.Domain.Common.TextCut.Max("ab", 5));

        var longShop = string.Concat(Enumerable.Repeat("\U0001F600", 600));
        var text = WhatsAppLinks.Message(OrderStatus.Ready, "Efua", longShop, "NB-1");
        Assert.True(text.Length <= WhatsAppLinks.MaxText);
        Assert.False(char.IsHighSurrogate(text[^1]));
        var url = WhatsAppLinks.Url("024 555 0123", text)!;
        Assert.DoesNotContain("%EF%BF%BD", url); // no broken character
    }

    [Fact]
    public void SentTime_IsKept_WhenDeliveryReceiptsArrive()
    {
        var t0 = new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
        var m = WhatsAppMessage.Create("m1", "b1", "o1", "c1", OrderStatus.Ready, t0, TimeSpan.Zero);
        m.MarkSent("wamid.1", t0.AddMinutes(1));
        Assert.True(m.ApplyDelivery(WhatsAppMessageState.Read, null, t0.AddHours(5)));
        Assert.Equal(t0.AddMinutes(1), m.SentAtUtc);
        Assert.Equal(t0.AddHours(5), m.UpdatedAtUtc);
    }

    [Fact]
    public void Webhook_ReadsStopTime_LatestPerNumber()
    {
        var body = "{\"entry\":[{\"changes\":[{\"value\":{\"messages\":[" +
                   "{\"from\":\"233245550123\",\"timestamp\":\"1793000000\",\"text\":{\"body\":\"STOP\"}}," +
                   "{\"from\":\"233245550123\",\"timestamp\":\"1793000100\",\"text\":{\"body\":\"stop\"}}," +
                   "{\"from\":\"233245550999\",\"timestamp\":\"junk\",\"text\":{\"body\":\"STOP\"}}]}}]}]}";
        var parsed = WhatsAppWebhook.Parse(System.Text.Encoding.UTF8.GetBytes(body));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793000100), parsed.StopAt["233245550123"]);
        Assert.False(parsed.StopAt.ContainsKey("233245550999")); // no usable time: server time is used
        Assert.Equal(3, parsed.StopFrom.Count);
    }
}
