using NeedleBase.Api.Hosting;

namespace NeedleBase.Web.Hosting;

/// <summary>
/// Builds, starts and stops the Api hosted in this process (Api:InProcess) together with the Web, and hands it the
/// "/api/..." requests. Built when the Web starts (not while the Web's own host is being built), so bad Api settings
/// still stop start-up with the Api's own message, and test hosts never mistake the Api's host for the Web's.
/// </summary>
public sealed class EmbeddedApiHostedService(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<EmbeddedApiHostedService> logger) : IHostedService
{
    private volatile EmbeddedApi? _api;
    private CancellationTokenSource? _afterStart;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var api = EmbeddedApi.Create(environment, configuration);
        try
        {
            await api.StartAsync(cancellationToken);
        }
        catch
        {
            // Create built a WebApplication: free it if StartAsync fails, otherwise the host's DI and background
            // services stay alive after the Web itself refuses to start.
            await api.DisposeAsync();
            throw;
        }

        _api = api;
        logger.LogInformation("Api hosted in this process at /api.");

        // Like the standalone Api: answer first, then Development migrate/seed or the pending-migrations warning.
        _afterStart = new CancellationTokenSource();
        var afterStart = _afterStart.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                afterStart.ThrowIfCancellationRequested();
                await api.AfterStartAsync();
            }
            catch (OperationCanceledException) when (afterStart.IsCancellationRequested)
            {
                // Shutting down before the check finished.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Api start-up checks failed.");
            }
        }, afterStart);
    }

    /// <summary>Runs the request through the Api; 503 (Retry-After) while it is starting or stopping.</summary>
    public Task HandleAsync(HttpContext context)
    {
        var api = _api;
        if (api is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "5";
            return Task.CompletedTask;
        }

        return api.HandleAsync(context);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var afterStart = Interlocked.Exchange(ref _afterStart, null);
        if (afterStart is not null)
        {
            await afterStart.CancelAsync();
            afterStart.Dispose();
        }

        var api = _api;
        _api = null;
        if (api is not null)
        {
            await api.StopAsync(cancellationToken);
            await api.DisposeAsync();
        }
    }
}
