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

    private static string CodeBox(string code) =>
        $"<p style=\"font-size:24px;font-weight:bold;letter-spacing:3px;font-family:monospace;background:#f3f1ec;padding:12px 16px;display:inline-block;border-radius:8px\">{H(code)}</p>";

    private static string Wrap(string inner) =>
        "<!doctype html><html><body style=\"font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:1.5;color:#222;max-width:520px;margin:0 auto;padding:16px\">" +
        inner + "<p>NeedleBase</p></body></html>";
}
