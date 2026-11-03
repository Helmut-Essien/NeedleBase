using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NeedleBase.Infrastructure.Licensing;

/// <summary>
/// Re-checks each licensed shop with Platform when its <c>refresh_after</c> is due (polls every
/// <see cref="PlatformOptions.RecheckPollMinutes"/>). Off while Platform isn't configured. Failures only reschedule.
/// </summary>
public sealed class LicenseRecheckWorker : BackgroundService
{
    public const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<PlatformOptions> _options;
    private readonly ILogger<LicenseRecheckWorker> _logger;

    public LicenseRecheckWorker(IServiceScopeFactory scopes, IOptionsMonitor<PlatformOptions> options, ILogger<LicenseRecheckWorker> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await DelayAsync(TimeSpan.FromSeconds(45), stoppingToken).ConfigureAwait(false)) // let startup/migrations finish
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.CurrentValue.IsConfigured)
                {
                    int seen;
                    var passes = 0;
                    do
                    {
                        using var scope = _scopes.CreateScope();
                        seen = await scope.ServiceProvider.GetRequiredService<LicenseService>()
                            .RecheckDueAsync(BatchSize, stoppingToken).ConfigureAwait(false);
                    }
                    while (seen >= BatchSize && ++passes < 20 && !stoppingToken.IsCancellationRequested);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "License re-check pass failed; will retry");
            }

            var minutes = Math.Clamp(_options.CurrentValue.RecheckPollMinutes, 1, 24 * 60);
            if (!await DelayAsync(TimeSpan.FromMinutes(minutes), stoppingToken).ConfigureAwait(false))
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
