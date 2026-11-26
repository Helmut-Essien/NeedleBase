using System.Globalization;
using System.Net;
using NeedleBase.Application.Abstractions;

namespace NeedleBase.Application.Email;

/// <summary>Short, plain emails that read well on a small phone and in Gmail's preview. Times shown in Ghana time (GMT).</summary>
public static class EmailTemplates
{
    public static EmailMessage Invite(string toEmail, string? toName, string businessName, string role, string code,
        DateTimeOffset expiresAtUtc, string? appBaseUrl)
    {
        toName = OneLine(toName);
        businessName = OneLine(businessName) ?? "";
        var greet = string.IsNullOrWhiteSpace(toName) ? "Hello" : $"Hello {toName.Trim()}";
        var article = role.StartsWith('A') || role.StartsWith('O') ? "an" : "a";
        var expires = FormatGmt(expiresAtUtc);
        var join = Link(appBaseUrl, "/Account/AcceptInvite");
        var text =
            $"{greet},\n\n{businessName} has invited you to join their team on NeedleBase as {article} {role}.\n\n" +
            $"Your invite code: {code}\n\n" +
            "To join: open the NeedleBase app and tap \"Join with an invite code\"" +
            (join is null ? "" : $", or go to {join}") + ", then enter the code.\n\n" +
            $"The code works once and expires on {expires}.\n\n" +
            "If you weren't expecting this, you can ignore this email.\n\nNeedleBase";
        var html = Wrap(
            $"<p>{H(greet)},</p><p><strong>{H(businessName)}</strong> has invited you to join their team on NeedleBase as {article} {H(role)}.</p>" +
            $"<p>Your invite code:</p>{CodeBox(code)}" +
            "<p>To join: open the NeedleBase app and tap <strong>Join with an invite code</strong>" +
            (join is null ? "" : $", or go to <a href=\"{H(join)}\">{H(join)}</a>") + ", then enter the code.</p>" +
            $"<p>The code works once and expires on {H(expires)}.</p>" +
            "<p style=\"color:#555\">If you weren't expecting this, you can ignore this email.</p>");
        return new EmailMessage(toEmail, toName, $"Join {businessName} on NeedleBase", text, html, "invite");
    }

    public static EmailMessage PasswordReset(string toEmail, string? toName, string code, TimeSpan validFor, string? appBaseUrl)
    {
        toName = OneLine(toName);
        var greet = string.IsNullOrWhiteSpace(toName) ? "Hello" : $"Hello {toName.Trim()}";
        var minutes = (int)validFor.TotalMinutes;
        var reset = Link(appBaseUrl, "/Account/ResetPassword");
        var text =
            $"{greet},\n\nSomeone asked to reset your NeedleBase password.\n\nYour reset code: {code}\n\n" +
            "Enter it in the NeedleBase app (Forgot password)" + (reset is null ? "" : $" or at {reset}") +
            $". It works once and expires in {minutes} minutes.\n\n" +
            "After the reset, every phone and browser signed in to your account is signed out.\n\n" +
            "If this wasn't you, ignore this email. Your password stays the same.\n\nNeedleBase";
        var html = Wrap(
            $"<p>{H(greet)},</p><p>Someone asked to reset your NeedleBase password.</p><p>Your reset code:</p>{CodeBox(code)}" +
            "<p>Enter it in the NeedleBase app (<strong>Forgot password</strong>)" +
            (reset is null ? "" : $" or at <a href=\"{H(reset)}\">{H(reset)}</a>") +
            $". It works once and expires in {minutes} minutes.</p>" +
            "<p>After the reset, every phone and browser signed in to your account is signed out.</p>" +
            "<p style=\"color:#555\">If this wasn't you, ignore this email. Your password stays the same.</p>");
        return new EmailMessage(toEmail, toName, "Your NeedleBase password reset code", text, html, "password_reset");
    }

    public static EmailMessage MonthlySummary(
        string toEmail,
        string? toName,
        string businessName,
        DateOnly monthStart,
        int activeOrders,
        int overdue,
        int dueNext7Days,
        int fittingsNext7Days,
        decimal outstanding,
        int ordersWithBalance,
        decimal receivedThisMonth,
        string? appBaseUrl)
    {
        toName = OneLine(toName);
        businessName = OneLine(businessName) ?? "";
        var greet = string.IsNullOrWhiteSpace(toName) ? "Hello" : $"Hello {toName.Trim()}";
        var monthLabel = monthStart.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"));
        var money = MoneyLine(outstanding, ordersWithBalance, receivedThisMonth, "this month");
        var home = Link(appBaseUrl, "/");
        var text =
            $"{greet},\n\nHere's your NeedleBase summary for {monthLabel} for {businessName}.\n\n" +
            $"Open orders: {activeOrders}\nOverdue: {overdue}\nDue in the next 7 days: {dueNext7Days}\n" +
            $"Fittings in the next 7 days: {fittingsNext7Days}\n\n{money}\n\n" +
            "Open NeedleBase on the web or your phone to see the details" +
            (home is null ? "" : $" ({home})") + ".\n\nNeedleBase";
        var html = Wrap(
            $"<p>{H(greet)},</p><p>Here's your NeedleBase summary for <strong>{H(monthLabel)}</strong> for <strong>{H(businessName)}</strong>.</p>" +
            "<ul>" +
            $"<li>Open orders: {activeOrders}</li><li>Overdue: {overdue}</li>" +
            $"<li>Due in the next 7 days: {dueNext7Days}</li><li>Fittings in the next 7 days: {fittingsNext7Days}</li>" +
            "</ul>" +
            $"<p>{HtmlBreaks(money)}</p>" +
            "<p>Open NeedleBase on the web or your phone to see the details" +
            (home is null ? "" : $" (<a href=\"{H(home)}\">{H(home)}</a>)") + ".</p>");
        return new EmailMessage(toEmail, toName, $"{businessName}: {monthLabel} summary", text, html, "monthly_summary");
    }

    public static EmailMessage WeeklySummary(
        string toEmail,
        string? toName,
        string businessName,
        DateOnly weekStart,
        int activeOrders,
        int overdue,
        int dueNext7Days,
        decimal outstanding,
        int ordersWithBalance,
        decimal receivedLast7Days,
        string? appBaseUrl)
    {
        toName = OneLine(toName);
        businessName = OneLine(businessName) ?? "";
        var greet = string.IsNullOrWhiteSpace(toName) ? "Hello" : $"Hello {toName.Trim()}";
        var weekEnd = weekStart.AddDays(6);
        var range = $"{weekStart.ToString("d MMM", CultureInfo.GetCultureInfo("en-GB"))} – {weekEnd.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB"))}";
        var balances = outstanding <= 0
            ? "Balances owed: none"
            : $"Balances owed: {FormatMoney(outstanding)} across {ordersWithBalance} order{(ordersWithBalance == 1 ? "" : "s")}";
        var received = $"Received in the last 7 days: {FormatMoney(receivedLast7Days)}";
        var home = Link(appBaseUrl, "/");
        var text =
            $"{greet},\n\nHere's your week for {businessName} ({range}).\n\n" +
            $"Open orders: {activeOrders}\nOverdue: {overdue}\nDue in the next 7 days: {dueNext7Days}\n\n" +
            $"{balances}\n{received}\n\n" +
            "Open NeedleBase on the web or your phone to see the details" +
            (home is null ? "" : $" ({home})") + ".\n\nNeedleBase";
        var html = Wrap(
            $"<p>{H(greet)},</p><p>Here's your week for <strong>{H(businessName)}</strong> ({H(range)}).</p>" +
            "<ul>" +
            $"<li>Open orders: {activeOrders}</li><li>Overdue: {overdue}</li>" +
            $"<li>Due in the next 7 days: {dueNext7Days}</li>" +
            "</ul>" +
            $"<p>{H(balances)}<br/>{H(received)}</p>" +
            "<p>Open NeedleBase on the web or your phone to see the details" +
            (home is null ? "" : $" (<a href=\"{H(home)}\">{H(home)}</a>)") + ".</p>");
        return new EmailMessage(toEmail, toName, $"{businessName}: week of {weekStart.ToString("d MMM", CultureInfo.GetCultureInfo("en-GB"))}", text, html, "weekly_summary");
    }

    public static EmailMessage OrderAssigned(
        string toEmail,
        string? toName,
        string businessName,
        string displayRef,
        string customerName,
        DateOnly? dueDate,
        string? appBaseUrl)
    {
        toName = OneLine(toName);
        businessName = OneLine(businessName) ?? "";
        customerName = OneLine(customerName) ?? "a customer";
        displayRef = OneLine(displayRef) ?? "";
        var greet = string.IsNullOrWhiteSpace(toName) ? "Hello" : $"Hello {toName.Trim()}";
        var due = dueDate is { } d
            ? d.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB"))
            : "No due date";
        var orders = Link(appBaseUrl, "/Orders");
        var text =
            $"{greet},\n\nYou've been given order #{displayRef} for {customerName} on {businessName}.\n" +
            $"Due: {due}\n\nOpen NeedleBase to see the order" +
            (orders is null ? "" : $" ({orders})") + ".\n\nNeedleBase";
        var html = Wrap(
            $"<p>{H(greet)},</p><p>You've been given order <strong>#{H(displayRef)}</strong> for <strong>{H(customerName)}</strong> on <strong>{H(businessName)}</strong>.</p>" +
            $"<p>Due: {H(due)}</p>" +
            "<p>Open NeedleBase to see the order" +
            (orders is null ? "" : $" (<a href=\"{H(orders)}\">{H(orders)}</a>)") + ".</p>");
        return new EmailMessage(toEmail, toName, $"{businessName}: order #{displayRef} assigned to you", text, html, "order_assigned");
    }

    /// <summary>
    /// Digest when many orders move to one person. Lists up to <paramref name="sampleLimit"/> refs; total count is always shown.
    /// </summary>
    public static EmailMessage OrdersAssignedBulk(
        string toEmail,
        string? toName,
        string businessName,
        int totalCount,
        IReadOnlyList<(string DisplayRef, string CustomerName)> samples,
        int sampleLimit,
        string? appBaseUrl)
    {
        toName = OneLine(toName);
        businessName = OneLine(businessName) ?? "";
        var greet = string.IsNullOrWhiteSpace(toName) ? "Hello" : $"Hello {toName.Trim()}";
        var take = Math.Clamp(sampleLimit, 1, 20);
        var shown = samples.Take(take).Select(s => (
            DisplayRef: OneLine(s.DisplayRef) ?? "",
            CustomerName: OneLine(s.CustomerName) ?? "a customer")).ToList();
        var more = totalCount > shown.Count ? $"\n…and {totalCount - shown.Count} more." : "";
        var moreHtml = totalCount > shown.Count
            ? $"<p>…and {totalCount - shown.Count} more.</p>"
            : "";
        var listText = string.Join("\n", shown.Select(s => $"#{s.DisplayRef} — {s.CustomerName}"));
        var listHtml = "<ul>" + string.Join("", shown.Select(s => $"<li><strong>#{H(s.DisplayRef)}</strong> — {H(s.CustomerName)}</li>")) + "</ul>";
        var orders = Link(appBaseUrl, "/Orders");
        var text =
            $"{greet},\n\nYou've been given {totalCount} order{(totalCount == 1 ? "" : "s")} on {businessName}.\n\n" +
            $"{listText}{more}\n\nOpen NeedleBase to see your orders" +
            (orders is null ? "" : $" ({orders})") + ".\n\nNeedleBase";
        var html = Wrap(
            $"<p>{H(greet)},</p><p>You've been given <strong>{totalCount}</strong> order{(totalCount == 1 ? "" : "s")} on <strong>{H(businessName)}</strong>.</p>" +
            listHtml + moreHtml +
            "<p>Open NeedleBase to see your orders" +
            (orders is null ? "" : $" (<a href=\"{H(orders)}\">{H(orders)}</a>)") + ".</p>");
        return new EmailMessage(toEmail, toName, $"{businessName}: {totalCount} order{(totalCount == 1 ? "" : "s")} assigned to you", text, html, "order_assigned_bulk");
    }

    public static EmailMessage OrderDue(
        string toEmail,
        string? toName,
        string businessName,
        string displayRef,
        string customerName,
        DateOnly dueDate,
        string? appBaseUrl)
    {
        toName = OneLine(toName);
        businessName = OneLine(businessName) ?? "";
        customerName = OneLine(customerName) ?? "a customer";
        displayRef = OneLine(displayRef) ?? "";
        var greet = string.IsNullOrWhiteSpace(toName) ? "Hello" : $"Hello {toName.Trim()}";
        var due = dueDate.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB"));
        var orders = Link(appBaseUrl, "/Orders");
        var text =
            $"{greet},\n\nOrder #{displayRef} for {customerName} is due today ({due}) at {businessName}.\n\n" +
            "Open NeedleBase to update it" +
            (orders is null ? "" : $" ({orders})") + ".\n\nNeedleBase";
        var html = Wrap(
            $"<p>{H(greet)},</p><p>Order <strong>#{H(displayRef)}</strong> for <strong>{H(customerName)}</strong> is due today ({H(due)}) at <strong>{H(businessName)}</strong>.</p>" +
            "<p>Open NeedleBase to update it" +
            (orders is null ? "" : $" (<a href=\"{H(orders)}\">{H(orders)}</a>)") + ".</p>");
        return new EmailMessage(toEmail, toName, $"{businessName}: order #{displayRef} is due today", text, html, "order_due");
    }

    /// <summary>"Mon 2 Nov 2026, 14:05 GMT" (Ghana is GMT all year).</summary>
    public static string FormatGmt(DateTimeOffset utc) =>
        utc.ToUniversalTime().ToString("ddd d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " GMT";

    private static string? Link(string? baseUrl, string path) =>
        Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var b) && (b.Scheme == Uri.UriSchemeHttps || b.Scheme == Uri.UriSchemeHttp)
            ? b.GetLeftPart(UriPartial.Authority) + b.AbsolutePath.TrimEnd('/') + path
            : null;

    /// <summary>Names go into the subject/To header: collapse line breaks and control characters to spaces.</summary>
    public static string? OneLine(string? s) =>
        s is null ? null : string.Join(' ', new string(s.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string H(string s) => WebUtility.HtmlEncode(s);

    private static string HtmlBreaks(string plain) =>
        string.Join("<br/>", plain.Split('\n').Select(H));

    private static string MoneyLine(decimal outstanding, int ordersWithBalance, decimal received, string receivedLabel) =>
        outstanding <= 0
            ? $"Money still owed: none\nReceived {receivedLabel}: {FormatMoney(received)}"
            : $"Money still owed: {FormatMoney(outstanding)} ({ordersWithBalance} order{(ordersWithBalance == 1 ? "" : "s")})\nReceived {receivedLabel}: {FormatMoney(received)}";

    private static string FormatMoney(decimal amount) =>
        "GH₵ " + amount.ToString("N2", CultureInfo.GetCultureInfo("en-GB"));

    private static string CodeBox(string code) =>
        $"<p style=\"font-size:24px;font-weight:bold;letter-spacing:3px;font-family:monospace;background:#f3f1ec;padding:12px 16px;display:inline-block;border-radius:8px\">{H(code)}</p>";

    private static string Wrap(string inner) =>
        "<!doctype html><html><body style=\"font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:1.5;color:#222;max-width:520px;margin:0 auto;padding:16px\">" +
        inner + "<p>NeedleBase</p></body></html>";
}
