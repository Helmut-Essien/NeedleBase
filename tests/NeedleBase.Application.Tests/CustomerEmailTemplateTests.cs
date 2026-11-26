using NeedleBase.Application.Email;

namespace NeedleBase.Application.Tests;

public class CustomerEmailTemplateTests
{
    [Fact]
    public void CustomerOrderStatus_Includes_Shop_And_Status()
    {
        var msg = EmailTemplates.CustomerOrderStatus(
            "efua@example.com", "Efua", "Ama Stitches", "+233200000000", "ABCD1234", "Ready to collect", null);
        Assert.Equal("customer_order_status", msg.Kind);
        Assert.Contains("Ready to collect", msg.TextBody);
        Assert.Contains("ABCD1234", msg.Subject);
        Assert.DoesNotContain("<script", msg.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CustomerPaymentReceipt_Matches_Share_Fields_And_Attaches_Pdf()
    {
        var msg = EmailTemplates.CustomerPaymentReceipt(
            "efua@example.com", "Efua", "Ama Stitches", "+233200000000", "ABCD1234",
            "26 Nov 2026", "GH₵ 300.00", "Mobile Money", "GH₵ 800.00", "GH₵ 300.00", "Balance: GH₵ 500.00");
        Assert.Equal("customer_payment_receipt", msg.Kind);
        Assert.Contains("Payment receipt", msg.TextBody);
        Assert.Contains("GH₵ 300.00", msg.TextBody);
        Assert.Contains("Balance: GH₵ 500.00", msg.TextBody);
        Assert.Contains("A PDF copy is attached.", msg.TextBody);
        Assert.NotNull(msg.Attachments);
        Assert.Single(msg.Attachments!);
        var pdf = msg.Attachments![0];
        Assert.Equal("receipt-ABCD1234.pdf", pdf.FileName);
        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.True(pdf.Content.Length > 100);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 4));
    }
}
