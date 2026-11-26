using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeedleBase.Application.Abstractions;

namespace NeedleBase.Infrastructure.Email;

/// <summary>
/// Polls for paid-plan due emails and Owner monthly/weekly summaries (after 06:00 Accra).
/// Off while SMTP is not configured. Durable watermarks live on <c>Business</c> / <c>Order</c>.
/// </summary>
public sealed class PlanEmailWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PlanEmailWorker> _logger;

    public PlanEmailWorker(IServiceScopeFactory scopes, ILogger<PlanEmailWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await DelayAsync(StartupDelay, stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                if (email.IsConfigured)
                {
                    var service = scope.ServiceProvider.GetRequiredService<PlanEmailService>();
                    var passes = 0;
                    int queued;
                    do
                    {
                        queued = await service.ProcessDueAsync(stoppingToken).ConfigureAwait(false);
                    }
                    while (queued > 0 && ++passes < 20 && !stoppingToken.IsCancellationRequested);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Paid-plan email pass failed; will retry");
            }

            if (!await DelayAsync(PollInterval, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan wait, CancellationToken token)
    {
        try
        {
            await Task.Delay(wait, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
