using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using NeedleBase.Application.Abstractions;

namespace NeedleBase.Infrastructure.Email;

/// <summary>Delivers one message. Separate from the queue so tests can swap it.</summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>MailKit SMTP with STARTTLS (Gmail: smtp.gmail.com:587 + App Password). Port 465 uses implicit TLS.</summary>
public sealed class SmtpTransport : IEmailTransport
{
    private readonly IOptionsMonitor<EmailOptions> _options;

    public SmtpTransport(IOptionsMonitor<EmailOptions> options) => _options = options;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var o = _options.CurrentValue;
        if (!o.IsConfigured)
        {
            throw new InvalidOperationException("Email is not configured.");
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.EffectiveFrom!));
        mime.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSeconds, 5, 120)));
        using var client = new SmtpClient { Timeout = Math.Clamp(o.TimeoutSeconds, 5, 120) * 1000 };
        var security = o.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(o.Host!, o.Port, security, timeout.Token);
        await client.AuthenticateAsync(o.Username!, o.Password!, timeout.Token);
        await client.SendAsync(mime, timeout.Token);
        try
        {
            await client.DisconnectAsync(true, timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Already accepted by the server: a failed goodbye must not trigger a retry (duplicate email).
        }
    }
}
