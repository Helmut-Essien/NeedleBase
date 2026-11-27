using System.Text.RegularExpressions;
using NeedleBase.Application.Email;
using NeedleBase.Application.Licensing;
using NeedleBase.Application.Receipts;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Application.Tests;

/// <summary>Code-gaps G15a/b: UK "licence" in prose, spaced Ghana phone numbers on receipts and emails.</summary>
public class PolishG15Tests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    // "License" is only allowed as the page's name ("the License page").
    private static readonly Regex UsSpelling = new(@"\blicense\b(?! page)", RegexOptions.IgnoreCase);

    [Fact]
    public void Banner_uses_uk_licence_key_and_keeps_the_page_name()
    {
        var shop = Business.Create("01JBUSINESSG15000000000001", "Ama Stitches", null, Now.AddDays(-1));
        var message = SubscriptionRules.Compute(shop, Now, 7).Message!;
        Assert.Contains("licence key", message);
        Assert.Contains("License page", message);
        Assert.DoesNotMatch(UsSpelling, message);
    }

    [Theory]
    [InlineData(LicenseErrors.KeyRequired)]
    [InlineData(LicenseErrors.KeyInvalid)]
    [InlineData(LicenseErrors.KeyInUse)]
    [InlineData(LicenseErrors.EmailMismatch)]
    [InlineData(LicenseErrors.Expired)]
    [InlineData(LicenseErrors.Suspended)]
    [InlineData(LicenseErrors.Revoked)]
    [InlineData(LicenseErrors.NotActive)]
    [InlineData(LicenseErrors.CheckUnavailable)]
    public void Licence_errors_use_uk_spelling(string code)
    {
        var message = LicenseErrors.MessageFor(code);
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotMatch(UsSpelling, message!);
        Assert.DoesNotMatch(UsSpelling, LicenseErrors.ReadOnlyMessage);
    }

    [Fact]
    public void Receipt_text_and_pdf_space_the_shop_phone()
    {
        var model = new PaymentReceiptShareModel("Ama Stitches", "0245550123", "Efua Mensah", "DWQ0MXHM", "26 Nov 2026",
            "GH₵ 100.00", "Cash", false, "GH₵ 800.00", "GH₵ 500.00", "GH₵ 300.00 left", false);
        Assert.Contains("\n+233 24 555 0123\n", PaymentReceiptShare.ToShareText(model));
        Assert.Contains("+233 24 555 0123", System.Text.Encoding.ASCII.GetString(PaymentReceiptShare.ToPdf(model)));
    }

    [Fact]
    public void Customer_emails_space_the_shop_phone_and_keep_other_numbers()
    {
        var receipt = EmailTemplates.CustomerPaymentReceipt("efua@example.test", "Efua", "Ama Stitches", "+233245550123", "DWQ0MXHM",
            "26 Nov 2026", "GH₵ 100.00", "Cash", "GH₵ 800.00", "GH₵ 500.00", "GH₵ 300.00 left");
        Assert.Contains("+233 24 555 0123", receipt.TextBody);
        Assert.Contains("+233 24 555 0123", receipt.HtmlBody);

        var status = EmailTemplates.CustomerOrderStatus("efua@example.test", "Efua", "Ama Stitches", "+44 20 7946 0958", "DWQ0MXHM", "Ready", null);
        Assert.Contains("Shop phone: +44 20 7946 0958", status.TextBody); // not a Ghana number: shown as entered
    }
}
