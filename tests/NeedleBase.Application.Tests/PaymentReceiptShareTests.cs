using System.Text;
using NeedleBase.Application.Receipts;

namespace NeedleBase.Application.Tests;

public class PaymentReceiptShareTests
{
    private static PaymentReceiptShareModel Sample(bool pending = true, bool voided = false) => new(
        BusinessName: "Ama Stitches",
        BusinessPhone: "+233200000000",
        CustomerName: "Kwame Mensah",
        OrderDisplayRef: "ABCD1234",
        DateText: "26 Nov 2026",
        AmountText: "GH₵ 300.00",
        MethodText: "Mobile Money",
        IsVoided: voided,
        OrderTotalText: "GH₵ 800.00",
        PaidSoFarText: "GH₵ 300.00",
        BalanceLine: "Balance: GH₵ 500.00",
        IsPending: pending);

    [Fact]
    public void ToShareText_Matches_Receipt_Fields_And_Omits_Note()
    {
        var text = PaymentReceiptShare.ToShareText(Sample());
        Assert.StartsWith("Ama Stitches\n+233200000000\nPayment receipt", text);
        Assert.Contains("Customer: Kwame Mensah", text);
        Assert.Contains("Order #ABCD1234", text);
        Assert.Contains("Amount: GH₵ 300.00", text);
        Assert.Contains("Method: Mobile Money", text);
        Assert.Contains("Balance: GH₵ 500.00", text);
        Assert.Contains("Recorded on this device; not yet synced.", text);
        Assert.DoesNotContain("Note", text);
    }

    [Fact]
    public void ToPdf_Is_Pdf_And_Contains_Share_Fields_As_Ghs()
    {
        var pdf = PaymentReceiptShare.ToPdf(Sample());
        Assert.True(pdf.Length > 200);
        var ascii = Encoding.ASCII.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", ascii);
        Assert.Contains("%%EOF", ascii);
        Assert.Contains("Ama Stitches", ascii);
        Assert.Contains("Kwame Mensah", ascii);
        Assert.Contains("ABCD1234", ascii);
        Assert.Contains("Mobile Money", ascii);
        Assert.Contains("GHS 300.00", ascii); // cedi glyph not in Helvetica
        Assert.DoesNotContain("GH₵", ascii);
        Assert.Contains("Recorded on this device; not yet synced.", ascii);
    }

    [Fact]
    public void ToPdf_Escapes_Parentheses_In_Names()
    {
        var m = Sample() with { CustomerName = "Ama (Senior)" };
        var ascii = Encoding.ASCII.GetString(PaymentReceiptShare.ToPdf(m));
        Assert.Contains(@"Ama \(Senior\)", ascii);
    }

    [Fact]
    public void ToShareText_Voided_Line()
    {
        var text = PaymentReceiptShare.ToShareText(Sample(pending: false, voided: true));
        Assert.Contains("This payment was voided.", text);
        Assert.DoesNotContain("not yet synced", text);
    }
}
