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

    // Explicit migrate in Development for local DX; Production should use CI/dotnet ef.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}
else
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                message = "An unexpected error occurred."
            });
        });
    });
    app.UseHsts();
}

app.UseHttpsRedirection();
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

app.Run();

static Task WriteMinimalHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString()
    });
}

public partial class Program;
