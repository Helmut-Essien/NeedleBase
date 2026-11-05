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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var api = EmbeddedApi.Create(environment, configuration);
        await api.StartAsync(cancellationToken);
        _api = api;
        logger.LogInformation("Api hosted in this process at /api.");

        // Like the standalone Api: answer first, then Development migrate/seed or the pending-migrations warning.
        _ = Task.Run(async () =>
        {
            try
            {
                await api.AfterStartAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Api start-up checks failed.");
            }
        }, CancellationToken.None);
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
        var api = _api;
        _api = null;
        if (api is not null)
        {
            await api.StopAsync(cancellationToken);
            await api.DisposeAsync();
        }
    }
}
