using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NeedleBase.Api.Middleware;
using NeedleBase.Infrastructure;
using NeedleBase.Infrastructure.Health;
using NeedleBase.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var ghanaianCulture = new CultureInfo("en-GH");
ghanaianCulture.NumberFormat.CurrencySymbol = "₵";
ghanaianCulture.NumberFormat.CurrencyDecimalDigits = 2;
CultureInfo.DefaultThreadCurrentCulture = ghanaianCulture;
CultureInfo.DefaultThreadCurrentUICulture = ghanaianCulture;

builder.Services.AddControllers();
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
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

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
app.UseRateLimiter();

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

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteMinimalHealthResponse
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteMinimalHealthResponse
}).AllowAnonymous();

app.MapControllers();

// Listen first so Api + Mobile launch prep can detect readiness; then migrate/seed.
await app.StartAsync();

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

await app.WaitForShutdownAsync();

static Task WriteMinimalHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString()
    });
}

public partial class Program;
