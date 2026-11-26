using System.Globalization;
using System.Text;

namespace NeedleBase.Application.Receipts;

/// <summary>
/// Plain-language payment receipt content shared by text share and on-device PDF.
/// Matches the mobile receipt preview (shop header, this payment, order totals).
/// The internal payment note never appears here.
/// </summary>
public sealed record PaymentReceiptShareModel(
    string BusinessName,
    string? BusinessPhone,
    string CustomerName,
    string OrderDisplayRef,
    string DateText,
    string AmountText,
    string MethodText,
    bool IsVoided,
    string OrderTotalText,
    string PaidSoFarText,
    string BalanceLine,
    bool IsPending);

public static class PaymentReceiptShare
{
    /// <summary>Lines for SMS/WhatsApp text share (same fields as the PDF body).</summary>
    public static IReadOnlyList<string> Lines(PaymentReceiptShareModel m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var lines = new List<string> { m.BusinessName.Trim() };
        if (!string.IsNullOrWhiteSpace(m.BusinessPhone))
        {
            lines.Add(m.BusinessPhone.Trim());
        }

        lines.Add("Payment receipt");
        lines.Add(string.Empty);
        lines.Add($"Customer: {m.CustomerName}");
        lines.Add($"Order #{m.OrderDisplayRef}");
        lines.Add($"Date: {m.DateText}");
        lines.Add($"Amount: {m.AmountText}");
        lines.Add($"Method: {m.MethodText}");
        if (m.IsVoided)
        {
            lines.Add("This payment was voided.");
        }

        lines.Add(string.Empty);
        lines.Add($"Order total: {m.OrderTotalText}");
        lines.Add($"Paid so far: {m.PaidSoFarText}");
        lines.Add(m.BalanceLine);
        if (m.IsPending)
        {
            lines.Add(string.Empty);
            lines.Add("Recorded on this device; not yet synced.");
        }

        return lines;
    }

    public static string ToShareText(PaymentReceiptShareModel m) => string.Join('\n', Lines(m));

    /// <summary>
    /// Minimal single-page PDF 1.4 (Helvetica). Generated on demand — not stored in blob storage.
    /// Non-ASCII characters are replaced so the built-in font stays valid.
    /// </summary>
    public static byte[] ToPdf(PaymentReceiptShareModel m)
    {
        var lines = Lines(m);
        const int pageW = 595;
        const int pageH = 842;
        const int left = 50;
        const int top = 780;
        const int leading = 16;
        const int fontSize = 11;

        var content = new StringBuilder(256);
        content.Append("BT\n/F1 ").Append(fontSize).Append(" Tf\n");
        content.Append(left.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(top.ToString(CultureInfo.InvariantCulture)).Append(" Td\n");
        content.Append(leading.ToString(CultureInfo.InvariantCulture)).Append(" TL\n");
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                content.Append("T*\n");
            }

            content.Append('(').Append(EscapePdfString(ForPdfFont(lines[i]))).Append(") Tj\n");
        }

        content.Append("ET\n");
        var streamBytes = Encoding.ASCII.GetBytes(content.ToString());

        using var ms = new MemoryStream();
        void WriteAscii(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        var offsets = new long[6]; // 1..5 used
        WriteAscii("%PDF-1.4\n");

        offsets[1] = ms.Position;
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = ms.Position;
        WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[3] = ms.Position;
        WriteAscii(
            $"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageW} {pageH}] " +
            "/Contents 5 0 R /Resources << /Font << /F1 4 0 R >> >> >>\nendobj\n");

        offsets[4] = ms.Position;
        WriteAscii("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        offsets[5] = ms.Position;
        WriteAscii($"5 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n");
        ms.Write(streamBytes, 0, streamBytes.Length);
        WriteAscii("\nendstream\nendobj\n");

        var xrefPos = ms.Position;
        WriteAscii("xref\n0 6\n");
        WriteAscii("0000000000 65535 f \n");
        for (var i = 1; i <= 5; i++)
        {
            WriteAscii(offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        WriteAscii($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
        return ms.ToArray();
    }

    /// <summary>Helvetica has no cedi glyph — keep amounts readable as GHS in the PDF only.</summary>
    private static string ForPdfFont(string line) =>
        line.Replace("GH₵", "GHS", StringComparison.Ordinal)
            .Replace("GH¢", "GHS", StringComparison.Ordinal)
            .Replace("₵", "", StringComparison.Ordinal)
            .Replace("¢", "", StringComparison.Ordinal);

    private static string EscapePdfString(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '\\' or '(' or ')')
            {
                sb.Append('\\').Append(ch);
            }
            else if (ch is >= ' ' and <= '~')
            {
                sb.Append(ch);
            }
            else if (ch is '\t')
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append('?');
            }
        }

        return sb.ToString();
    }
}
