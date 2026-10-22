namespace NeedleBase.Application.Audit;

/// <summary>
/// Who/where for the current request (scoped). Filled by the Api middleware from the JWT and HTTP request; read by
/// the DbContext when it writes audit rows. Background jobs leave it empty (source "system").
/// </summary>
public sealed class AuditContext
{
    public string? UserId { get; set; }
    public string? Role { get; set; }
    public string? BusinessId { get; set; }
    public string Source { get; set; } = Domain.Entities.AuditSource.System;
    public string? DeviceId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
}

/// <summary>One security/activity event (not a data change; those are captured by the DbContext automatically).</summary>
public sealed record AuditEntry(
    string Action,
    string Summary,
    string? BusinessId = null,
    string? ActorUserId = null,
    string? ActorRole = null,
    string? EntityType = null,
    string? EntityId = null);

/// <summary>
/// Writes security/activity events in their own short transaction (never inside, and never flushing, the caller's
/// unit of work). Best effort: a failure is logged and swallowed so it can never fail sign-in or sync.
/// <paramref name="throttleKey"/>: at most one event per key per <paramref name="window"/> (noisy events);
/// the next written event notes how many were folded into it.
/// </summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, string? throttleKey = null, TimeSpan? window = null, CancellationToken cancellationToken = default);
}
