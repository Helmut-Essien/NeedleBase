using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Common;
using NeedleBase.Domain.Entities;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Audit;

/// <summary>
/// Security/activity events in their own context and transaction: never part of (or flushing) the caller's unit
/// of work, so a failed sign-in is recorded even though nothing else is saved. Best effort by design: an audit
/// outage is logged and never turns a sign-in, sync or page view into an error.
/// </summary>
public sealed class AuditLog : IAuditLog
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly AuditContext _context;
    private readonly AuditThrottle _throttle;
    private readonly IClock _clock;
    private readonly ILogger<AuditLog> _logger;

    public AuditLog(DbContextOptions<ApplicationDbContext> options, AuditContext context, AuditThrottle throttle, IClock clock, ILogger<AuditLog> logger)
    {
        _options = options;
        _context = context;
        _throttle = throttle;
        _clock = clock;
        _logger = logger;
    }

    public async Task WriteAsync(AuditEntry entry, string? throttleKey = null, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = _clock.UtcNow;
            var summary = entry.Summary;
            if (throttleKey is not null)
            {
                if (!_throttle.TryEnter(throttleKey, window ?? TimeSpan.FromHours(1), now, out var folded))
                {
                    return;
                }

                if (folded > 0)
                {
                    summary += $" (+{folded} more since the last entry)";
                }
            }

            await using var db = new ApplicationDbContext(_options) { AuditEnabled = false };
            db.AuditEvents.Add(AuditEvent.Create(
                IdGenerator.NewId(),
                entry.BusinessId ?? _context.BusinessId,
                entry.Action,
                summary,
                Application.Sync.SyncTime.Normalize(now),
                actorUserId: entry.ActorUserId ?? _context.UserId,
                actorRole: entry.ActorRole ?? _context.Role,
                entityType: entry.EntityType,
                entityId: entry.EntityId,
                source: _context.Source,
                deviceId: _context.DeviceId,
                ipAddress: _context.IpAddress,
                userAgent: _context.UserAgent,
                correlationId: _context.CorrelationId));
            // Not cancellable by the caller: a sign-in that was answered must still be recorded.
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit event {Action} could not be written", entry.Action);
        }
    }
}
