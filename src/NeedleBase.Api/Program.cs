using NeedleBase.Api.Hosting;

namespace NeedleBase.Api;

/// <summary>
/// The Api on its own host: local development (Api + Mobile launch prep) and the integration tests
/// (WebApplicationFactory). In production the Web process hosts the same Api under /api (<see cref="EmbeddedApi"/>).
/// Namespaced so it never clashes with the Web's <c>Program</c> (the Web references this project).
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var app = ApiApplication.Build(WebApplication.CreateBuilder(args));

        // Listen first so Api + Mobile launch prep can detect readiness; then migrate/seed.
        await app.StartAsync();
        await ApiApplication.AfterStartAsync(app);
        await app.WaitForShutdownAsync();
    }
}
