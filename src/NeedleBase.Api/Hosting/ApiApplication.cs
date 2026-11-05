using Microsoft.AspNetCore.DataProtection;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NeedleBase.Api.Configuration;
using NeedleBase.Api.Middleware;
using NeedleBase.Infrastructure;
using NeedleBase.Infrastructure.Health;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Hosting;

/// <summary>
/// The whole Api (services + middleware pipeline). Used by <see cref="Program"/> when the Api runs on its own (local
/// development, integration tests) and by <see cref="EmbeddedApi"/> when the Web process hosts it under /api on the
/// one production site (docs/deployment.md).
/// </summary>
public static class ApiApplication
{
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        // Production/Staging: stop at start-up with one clear list of missing or wrong settings (docs/production-config.md).
        // Development and the "Testing" environment (integration tests) skip it.
        var configWarnings = new List<string>();
        if (ProductionConfigValidator.ShouldValidate(builder.Environment))
        {
            ProductionConfigValidator.ThrowIfInvalid(builder.Configuration, configWarnings.Add);
        }

        var ghanaianCulture = new CultureInfo("en-GH");
        ghanaianCulture.NumberFormat.CurrencySymbol = "₵";
        ghanaianCulture.NumberFormat.CurrencyDecimalDigits = 2;
        CultureInfo.DefaultThreadCurrentCulture = ghanaianCulture;
        CultureInfo.DefaultThreadCurrentUICulture = ghanaianCulture;

        builder.Services.AddControllers(options => options.Filters.Add<NeedleBase.Api.Http.SubscriptionWriteGuard>());
        // RFC 9457 ProblemDetails for every error (model validation, 404/401/429 status codes, unhandled exceptions).
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Extensions.TryAdd("traceId", ctx.HttpContext.TraceIdentifier);
                if (!ctx.ProblemDetails.Extensions.ContainsKey("code"))
                {
                    ctx.ProblemDetails.Extensions["code"] = ctx.ProblemDetails.Status switch
                    {
                        StatusCodes.Status400BadRequest => "VALIDATION_FAILED",
                        StatusCodes.Status401Unauthorized => "UNAUTHORIZED",
                        StatusCodes.Status403Forbidden => "FORBIDDEN",
                        StatusCodes.Status404NotFound => "NOT_FOUND",
                        StatusCodes.Status429TooManyRequests => "RATE_LIMITED",
                        StatusCodes.Status500InternalServerError => "SERVER_ERROR",
                        _ => "ERROR"
                    };
                }

                // Never leak exception text to clients outside Development.
                if (ctx.ProblemDetails.Status >= 500 && !ctx.HttpContext.RequestServices
                        .GetRequiredService<IWebHostEnvironment>().IsDevelopment())
                {
                    ctx.ProblemDetails.Title = "Something went wrong";
                    ctx.ProblemDetails.Detail = "Something went wrong on our side. Please try again.";
                }
            };
        });
        builder.Services.AddOpenApi();
        // Android in-app updates (GET api/mobile/update): newest GitHub Release's update.json, cached (AssetTag style).
        builder.Services.AddHttpClient(NeedleBase.Api.Updates.MobileUpdateFeed.HttpClientName);
        builder.Services.AddSingleton<NeedleBase.Api.Updates.MobileUpdateFeed>();
        builder.Services.AddInfrastructure(builder.Configuration);

        // Data Protection encrypts each shop's license key at rest. The key ring must survive restarts/redeploys, or stored
        // license keys can't be read (the shop is NOT locked then; the Owner re-enters the key). DataProtection:KeysPath
        // overrides the location; production defaults to App_Data/Keys/Api. Development uses the user-profile key ring.
        {
            var keysPath = builder.Configuration["DataProtection:KeysPath"];
            if (string.IsNullOrWhiteSpace(keysPath) && builder.Environment.IsProduction())
            {
                keysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Keys", "Api");
            }

            var dataProtection = builder.Services.AddDataProtection().SetApplicationName("NeedleBase.Api");
            if (!string.IsNullOrWhiteSpace(keysPath))
            {
                DirectoryInfo keysDirectory;
                try
                {
                    keysDirectory = Directory.CreateDirectory(keysPath.Trim());
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    throw new InvalidOperationException(
                        $"Data Protection key folder can't be created or written ({ex.GetType().Name}). Give the app pool write access " +
                        "or set DataProtection__KeysPath to a writable absolute folder (docs/production-config.md).", ex);
                }

                dataProtection.PersistKeysToFileSystem(keysDirectory);
            }
        }

        // Auth attempts per IP per minute. Configurable (RateLimits:AuthPermitPerMinute) so integration tests, which
        // register many accounts from one address, are not throttled; production keeps the default.
        var authPermitPerMinute = builder.Configuration.GetValue("RateLimits:AuthPermitPerMinute", 30);
        // Token refresh is routine (every app start and every expired access token, from a whole shop behind one IP), so it
        // has its own, larger per-IP budget and never eats into the login/register budget.
        var refreshPermitPerMinute = builder.Configuration.GetValue("RateLimits:RefreshPermitPerMinute", 300);
        var registerPermitPer10Minutes = builder.Configuration.GetValue("RateLimits:RegisterPermitPer10Minutes", 10);

        // Behind a reverse proxy / the web front end, the client IP arrives in X-Forwarded-For. It is trusted only from
        // loopback (default) plus ForwardedHeaders:KnownProxies (IPs) / KnownNetworks (CIDR), comma-separated.
        builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
        {
            ForwardedHeadersConfig.Apply(options, builder.Configuration);
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authPermitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // Registration / license attach call Platform: much stricter than sign-in (per IP, 10-minute window).
            options.AddPolicy(AuthRateLimits.RegisterPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = registerPermitPer10Minutes,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0
                    }));

            options.AddPolicy(AuthRateLimits.RefreshPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = refreshPermitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // Sync: per signed-in user (not per IP: a whole shop can share one IP). A token bucket lets a fresh device
            // pull a big book in a burst, then sustains SyncRateLimit.TokensPerSecond; the app waits Retry-After and continues.
            options.AddPolicy(SyncRateLimit.PolicyName, httpContext =>
                RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey: SyncRateLimit.PartitionKey(httpContext),
                    factory: _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = SyncRateLimit.Burst,
                        TokensPerPeriod = SyncRateLimit.TokensPerSecond,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                        ? ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture)
                        : "1";
                return ValueTask.CompletedTask;
            };
        });

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        var app = builder.Build();

        foreach (var warning in configWarnings)
        {
            app.Logger.LogWarning("Configuration: {Warning}", warning);
        }

        // Keep "/api" in the path when IIS (or a reverse proxy) stripped it as a PathBase, so routes match (ApiPathBase).
        app.Use((context, next) =>
        {
            ApiPathBase.Restore(context.Request);
            return next(context);
        });

        // Our own Web front end forwards the visitor's IP. If it reaches the Api from an address that is not trusted (on a
        // single shared site the call leaves and re-enters through the host's network), every web visitor shares that one
        // address in the per-IP limits: say so once, with the address to add to ForwardedHeaders__KnownProxies.
        {
            var forwardedOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>>().Value;
            var warnedUntrustedWebPeer = 0;
            app.Use((context, next) =>
            {
                if (Volatile.Read(ref warnedUntrustedWebPeer) == 0 &&
                    context.Request.Headers.ContainsKey("X-Forwarded-For") &&
                    string.Equals(context.Request.Headers["X-NeedleBase-Client"], "web", StringComparison.Ordinal) &&
                    !ForwardedHeadersConfig.IsTrusted(forwardedOptions, context.Connection.RemoteIpAddress) &&
                    Interlocked.Exchange(ref warnedUntrustedWebPeer, 1) == 0)
                {
                    app.Logger.LogWarning(
                        "The Web front end reaches the Api from {Peer}, which is not a trusted proxy, so all web visitors share that address in the " +
                        "sign-in rate limits. If it is this host's own address, add it to ForwardedHeaders__KnownProxies on the Api (docs/production-config.md).",
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                }

                return next(context);
            });
        }

        // Next, so every later step (rate limits, login throttle, refresh-token IP) sees the real client IP.
        app.UseForwardedHeaders();

        // Explicit, so endpoints are matched AFTER ApiPathBase.Restore (the implicit UseRouting would run first and lose
        // every "api/..." controller route in the /api sub-application). Still ahead of everything else, as before.
        app.UseRouting();

        // The browser/web app hung up (navigated away, clicked again, closed the tab): the request's own token cancels the
        // work and an OperationCanceledException unwinds. That is normal, not an error: answer 499 quietly (nobody reads it)
        // instead of an error log + debugger break. Real timeouts are not caught here (the caller is still waiting).
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = 499; // "client closed request"
                }
            }
        });

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
        else
        {
            app.UseExceptionHandler();
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseStatusCodePages();
        // Map mobile X-Auth-Token → Authorization (must run before authentication).
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Auth-Token", out var customToken) &&
                !context.Request.Headers.ContainsKey("Authorization"))
            {
                context.Request.Headers.Authorization = customToken.ToString();
            }

            await next();
        });

        app.UseAuthentication();
        app.UseMiddleware<BusinessTenancyMiddleware>();
        app.UseAuthorization();

        // After authentication so sync limits can be per user.
        app.UseRateLimiter();

        // "/health/*" when the Api is the host root; "/api/health/*" when it is served under /api (embedded or PathBase).
        foreach (var prefix in new[] { "", ApiPathBase.Segment })
        {
            app.MapHealthChecks(prefix + "/health/live", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live"),
                ResponseWriter = WriteMinimalHealthResponse
            }).AllowAnonymous();

            app.MapHealthChecks(prefix + "/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready"),
                ResponseWriter = WriteMinimalHealthResponse
            }).AllowAnonymous();
        }

        app.MapControllers();

        return app;
    }

    /// <summary>After the host is listening: Development migrate/seed; elsewhere only warn about pending migrations.</summary>
    public static async Task AfterStartAsync(WebApplication app)
    {

        if (app.Environment.IsDevelopment())
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.MigrateAsync();
                await DevelopmentDataSeeder.SeedAsync(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(
                    ex,
                    "Development migrate/seed failed. Api is listening, but DB features will not work until PostgreSQL is reachable.");
            }
        }
        else if (!app.Environment.IsEnvironment("Testing"))
        {
            // Production never migrates itself (several instances / a half-applied deploy could race). The deploy step applies
            // scripts/db-migrate.sh output first (docs/deployment.md); this only shouts if that step was skipped.
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
                if (pending.Count > 0)
                {
                    app.Logger.LogCritical(
                        "Database is {Count} migration(s) behind ({Latest}). Apply the idempotent migration script before using this release (docs/deployment.md).",
                        pending.Count, pending[^1]);
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Could not check pending database migrations (is PostgreSQL reachable?).");
            }
        }
    }

    private static Task WriteMinimalHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString()
        });
    }
}
