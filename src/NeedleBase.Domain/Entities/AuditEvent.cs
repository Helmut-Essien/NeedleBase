namespace NeedleBase.Domain.Entities;

/// <summary>
/// One append-only audit/activity record. Tenant-scoped by <see cref="BusinessId"/> (null only for security events
/// that can't be tied to a business, e.g. a sign-in attempt for an unknown email). Never updated or deleted by the
/// application (a database trigger refuses it; only the retention purge may delete). Never synced to devices.
/// </summary>
public sealed class AuditEvent : Entity
{
    public const int SummaryMax = 300;
    public const int ActionMax = 64;
    public const int UserAgentMax = 256;

    private AuditEvent()
    {
    }

    public string? BusinessId { get; private set; }
    public string? ActorUserId { get; private set; }
    public string? ActorRole { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }

    /// <summary>Denormalised for per-order history (payments and lines of the order too).</summary>
    public string? OrderId { get; private set; }

    /// <summary>Denormalised for per-customer history (their measurements, orders and payments too).</summary>
    public string? CustomerId { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    /// <summary>JSON array of {"field","old","new"} with masked/omitted sensitive values; null when none.</summary>
    public string? ChangesJson { get; private set; }

    public string Source { get; private set; } = AuditSource.System;
    public string? DeviceId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static AuditEvent Create(
        string id,
        string? businessId,
        string action,
        string summary,
        DateTimeOffset occurredAtUtc,
        string? actorUserId = null,
        string? actorRole = null,
        string? entityType = null,
        string? entityId = null,
        string? orderId = null,
        string? customerId = null,
        string? changesJson = null,
        string? source = null,
        string? deviceId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return new AuditEvent
        {
            Id = id,
            BusinessId = businessId,
            Action = Cut(action, ActionMax)!,
            Summary = Cut(string.IsNullOrWhiteSpace(summary) ? action : summary.Trim(), SummaryMax)!,
            OccurredAtUtc = occurredAtUtc,
            ActorUserId = Cut(actorUserId, 64),
            ActorRole = Cut(actorRole, 32),
            EntityType = Cut(entityType, 32),
            EntityId = Cut(entityId, 64),
            OrderId = Cut(orderId, 64),
            CustomerId = Cut(customerId, 64),
            ChangesJson = changesJson,
            Source = AuditSource.IsValid(source) ? source! : AuditSource.System,
            DeviceId = Cut(deviceId, 64),
            IpAddress = Cut(ipAddress, 64),
            UserAgent = Cut(userAgent, UserAgentMax),
            CorrelationId = Cut(correlationId, 64)
        };
    }

    private static string? Cut(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var v = value.Trim();
        if (v.Length <= max)
        {
            return v;
        }

        // Never split an emoji/surrogate pair: a lone surrogate can't be encoded and would fail the save.
        var keep = max - 1;
        if (char.IsHighSurrogate(v[keep - 1]))
        {
            keep--;
        }

        return v[..keep] + "…";
    }
}

/// <summary>Where an audited change came from.</summary>
public static class AuditSource
{
    public const string Api = "api";
    public const string Web = "web";
    public const string Sync = "sync";
    public const string System = "system";

    public static readonly IReadOnlyList<string> All = [Api, Web, Sync, System];

    public static bool IsValid(string? source) => source is not null && All.Contains(source);
}
