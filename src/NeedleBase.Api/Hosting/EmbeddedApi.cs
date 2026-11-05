using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NeedleBase.Api.Hosting;

/// <summary>
/// The Api hosted inside another ASP.NET Core process (the Web), so ONE IIS application serves the site: the Web at
/// "/" and the Api at "/api" (docs/deployment.md). The Api keeps its own host: its own DI container, configuration
/// binding, JWT authentication, Data Protection key ring, rate limits, health checks and background services, so
/// nothing is shared with the Web's cookie/antiforgery set-up. Instead of Kestrel it gets an in-memory server; the
/// Web hands every "/api/..." request to it (<see cref="HandleAsync"/>) with the path unchanged, so Api routes and
/// public URLs (<c>{site}/api/...</c>) are exactly those of the standalone Api.
/// </summary>
public sealed class EmbeddedApi : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly InProcessServer _server;

    private EmbeddedApi(WebApplication app, InProcessServer server)
    {
        _app = app;
        _server = server;
    }

    /// <summary>
    /// Builds the Api with the host's environment, content root and configuration (one site = one set of settings:
    /// the Web's appsettings.json, web.config environment variables, ...). Throws like the standalone Api on bad
    /// production settings.
    /// </summary>
    public static EmbeddedApi Create(IWebHostEnvironment hostEnvironment, IConfiguration hostConfiguration)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // The Api's assembly: MVC finds its controllers there, user secrets (Development) are the Api's.
            ApplicationName = typeof(EmbeddedApi).Assembly.GetName().Name,
            EnvironmentName = hostEnvironment.EnvironmentName,
            ContentRootPath = hostEnvironment.ContentRootPath
        });

        // Exactly the host's settings (they already include environment variables and the command line).
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddConfiguration(hostConfiguration);
        // One DataProtection:KeysPath serves both rings: the Web keeps its keys there, the Api in its own "Api"
        // subfolder (as with the default App_Data/Keys and App_Data/Keys/Api), so the two never share a folder.
        var keysPath = hostConfiguration["DataProtection:KeysPath"]?.Trim();
        if (!string.IsNullOrEmpty(keysPath) && Path.IsPathFullyQualified(keysPath))
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeysPath"] = Path.Combine(keysPath, "Api"),
            });

        var server = new InProcessServer();
        builder.Services.RemoveAll<IServer>();
        builder.Services.AddSingleton<IServer>(server);
        // The outer host owns Ctrl+C/SIGTERM and IIS; the Api stops when the outer host stops it.
        builder.Services.RemoveAll<IHostLifetime>();
        builder.Services.AddSingleton<IHostLifetime, OuterHostLifetime>();
        RemoveIisStartupFilters(builder.Services);

        return new EmbeddedApi(ApiApplication.Build(builder), server);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _app.StartAsync(cancellationToken);
    }

    /// <summary>Development migrate/seed, or the pending-migrations warning (same as the standalone Api).</summary>
    public Task AfterStartAsync() => ApiApplication.AfterStartAsync(_app);

    public Task StopAsync(CancellationToken cancellationToken) => _app.StopAsync(cancellationToken);

    /// <summary>Runs the request through the Api's pipeline. 503 until the Api has started.</summary>
    public Task HandleAsync(HttpContext context) => _server.HandleAsync(context);

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    /// <summary>"/api" or "/api/..." (case-insensitive): the requests the Api answers on the shared site.</summary>
    public static bool IsApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments(Configuration.ApiPathBase.Segment, StringComparison.OrdinalIgnoreCase);

    // The outer host already ran IIS integration (pairing token, forwarded scheme/IP, path base) for the request.
    private static void RemoveIisStartupFilters(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var d = services[i];
            if (d.ServiceType != typeof(IStartupFilter))
            {
                continue;
            }

            var type = d.ImplementationType ?? d.ImplementationInstance?.GetType();
            if (type?.Namespace?.StartsWith("Microsoft.AspNetCore.Server.IIS", StringComparison.Ordinal) == true)
            {
                services.RemoveAt(i);
            }
        }
    }

    private sealed class OuterHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>An <see cref="IServer"/> that listens nowhere: requests arrive from the outer host's pipeline.</summary>
    private sealed class InProcessServer : IServer
    {
        private volatile Func<IFeatureCollection, Task>? _process;

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull
        {
            _process = async features =>
            {
                var context = application.CreateContext(features);
                try
                {
                    await application.ProcessRequestAsync(context);
                }
                catch (Exception ex)
                {
                    application.DisposeContext(context, ex);
                    throw;
                }

                application.DisposeContext(context, null);
            };
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _process = null;
            return Task.CompletedTask;
        }

        public void Dispose() => _process = null;

        public Task HandleAsync(HttpContext outer)
        {
            var process = _process;
            if (process is null)
            {
                outer.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                outer.Response.Headers.RetryAfter = "5";
                return Task.CompletedTask;
            }

            // A layer over the outer request's features: the connection, request and response are shared, while the
            // Api's own per-request features (request services, authentication, routing, ...) never overwrite the
            // outer host's.
            return process(new FeatureCollection(outer.Features));
        }
    }
}
