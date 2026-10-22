using NeedleBase.Application.Abstractions;
using NeedleBase.Infrastructure.Persistence;
using NeedleBase.Application.Audit;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Api.Middleware;

/// <summary>
/// Sets <see cref="ApplicationDbContext.CurrentBusinessId"/> from JWT so global query filters apply.
/// </summary>
public sealed class BusinessTenancyMiddleware
{
    private readonly RequestDelegate _next;

    public BusinessTenancyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Sent by the NeedleBase web front end so its writes are labelled "web" in the activity log.</summary>
    public const string ClientHeader = "X-NeedleBase-Client";

    /// <summary>Optional device id (the phone's sync DeviceId) for the activity log.</summary>
    public const string DeviceHeader = "X-Device-Id";

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db, ICurrentUser currentUser, AuditContext audit)
    {
        if (!string.IsNullOrEmpty(currentUser.BusinessId))
        {
            db.CurrentBusinessId = currentUser.BusinessId;
        }

        // Who/where for audit rows. The client IP is the forwarded one (UseForwardedHeaders runs first and trusts
        // only known proxies). The client/device headers are labels only: they never grant anything.
        audit.UserId = currentUser.UserId;
        audit.Role = currentUser.Role;
        audit.BusinessId = currentUser.BusinessId;
        var ip = context.Connection.RemoteIpAddress;
        audit.IpAddress = (ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip)?.ToString(); // "::ffff:1.2.3.4" → "1.2.3.4"
        audit.UserAgent = context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
        audit.CorrelationId = context.Request.Headers["X-Correlation-Id"].ToString() is { Length: > 0 and <= 64 } cid ? cid : context.TraceIdentifier;
        audit.DeviceId = context.Request.Headers[DeviceHeader].ToString() is { Length: > 0 and <= 64 } device ? device : null;
        audit.Source = context.Request.Path.StartsWithSegments("/api/sync")
            ? AuditSource.Sync
            : string.Equals(context.Request.Headers[ClientHeader], "web", StringComparison.OrdinalIgnoreCase) ? AuditSource.Web : AuditSource.Api;
        db.AuditContext = audit;

        await _next(context);
    }
}
