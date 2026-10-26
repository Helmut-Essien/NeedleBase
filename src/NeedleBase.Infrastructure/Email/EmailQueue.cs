using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Abstractions;

namespace NeedleBase.Infrastructure.Email;

/// <summary>
/// In-memory outbox: requests return at once and a background worker sends, so slow SMTP never blocks a screen.
/// A restart drops unsent mail (the user can ask again: new reset code / resend invite). Bounded so a burst can't eat
/// memory; Gmail caps sending at about 500 a day anyway.
/// </summary>
public sealed class EmailQueue : IEmailSender
{
    public const int Capacity = 200;

    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly Channel<QueuedEmail> _channel =
        Channel.CreateBounded<QueuedEmail>(new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public EmailQueue(IOptionsMonitor<EmailOptions> options) => _options = options;

    public bool IsConfigured => _options.CurrentValue.IsConfigured;

    public bool TryQueue(EmailMessage message) => IsConfigured && _channel.Writer.TryWrite(new QueuedEmail(message, 0));

    internal bool TryRequeue(QueuedEmail item) => _channel.Writer.TryWrite(item);

    internal ChannelReader<QueuedEmail> Reader => _channel.Reader;

    internal int Pending => _channel.Reader.Count;
}

internal sealed record QueuedEmail(EmailMessage Message, int Attempt);

/// <summary>
/// Sends queued mail one at a time; a failed message is retried later (5 s, then 30 s) without holding up the
/// messages behind it, so one bad address can't delay someone's reset code. Logs never include bodies or the password.
/// </summary>
public sealed class EmailSendingService : BackgroundService
{
    public static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

    private readonly EmailQueue _queue;
    private readonly IEmailTransport _transport;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<EmailSendingService> _logger;

    public EmailSendingService(EmailQueue queue, IEmailTransport transport, IOptionsMonitor<EmailOptions> options, ILogger<EmailSendingService> logger)
    {
        _queue = queue;
        _transport = transport;
        _options = options;
        _logger = logger;
    }

    /// <summary>Retry pauses (tests shorten them).</summary>
    internal TimeSpan[] Delays { get; set; } = RetryDelays;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var o = _options.CurrentValue;
        if (o.IsConfigured)
        {
            _logger.LogInformation("Email sending is on: {Host}:{Port} as {User}.", o.Host, o.Port, Mask(o.Username!));
        }
        else
        {
            _logger.LogWarning(
                "Email is not configured (Email:Host, Email:Username, Email:Password). Invite codes must be shared by hand and Forgot password stays hidden. " +
                "Set them with dotnet user-secrets or Email__* environment variables.");
        }

        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_queue.Pending > 0)
        {
            _logger.LogWarning("Shutting down with {Count} unsent email(s); they are dropped (people can ask again).", _queue.Pending);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await SendOnceAsync(item, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>One try. On failure schedules the next try in the background (true = sent).</summary>
    internal async Task<bool> SendOnceAsync(QueuedEmail item, CancellationToken stoppingToken)
    {
        var message = item.Message;
        try
        {
            await _transport.SendAsync(message, stoppingToken);
            _logger.LogInformation("Sent {Kind} email to {To}.", message.Kind, Mask(message.ToAddress));
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Exception type + message only (MailKit messages carry no credentials or bodies).
            if (item.Attempt >= Delays.Length)
            {
                _logger.LogError("Gave up sending {Kind} email to {To}: {Error} ({Type}).", message.Kind, Mask(message.ToAddress), ex.Message, ex.GetType().Name);
                return false;
            }

            _logger.LogWarning("Sending {Kind} email to {To} failed, will retry: {Error} ({Type}).", message.Kind, Mask(message.ToAddress), ex.Message, ex.GetType().Name);
            var delay = Delays[item.Attempt];
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                    if (!_queue.TryRequeue(item with { Attempt = item.Attempt + 1 }))
                    {
                        _logger.LogError("Dropped {Kind} email to {To}: send queue full.", message.Kind, Mask(message.ToAddress));
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, CancellationToken.None);
            return false;
        }
    }

    private static string Mask(string email) => NeedleBase.Application.Audit.AuditMask.Email(email) ?? "(hidden)";
}
