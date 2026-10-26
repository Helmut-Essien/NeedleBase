namespace NeedleBase.Application.Abstractions;

/// <summary>One outgoing email (plain text + simple HTML). Never logged: bodies can hold one-time codes.</summary>
public sealed record EmailMessage(string ToAddress, string? ToName, string Subject, string TextBody, string HtmlBody, string Kind);

/// <summary>
/// Sends email off the request path. <see cref="IsConfigured"/> is false until SMTP settings (incl. the password) are
/// set; callers then keep the manual flow (codes shared by hand, Forgot password hidden).
/// </summary>
public interface IEmailSender
{
    bool IsConfigured { get; }

    /// <summary>Queues the message; false when email isn't configured or the queue is full (caller carries on).</summary>
    bool TryQueue(EmailMessage message);
}
