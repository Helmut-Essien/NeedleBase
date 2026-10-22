using System.ComponentModel.DataAnnotations;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.Payments;

namespace NeedleBase.Application.Contracts.Audit;

/// <summary>One changed field (values already masked server-side; null = empty).</summary>
public sealed record AuditChangeDto(string Field, string? Old, string? New)
{
    public string FieldDisplay => AuditActions.FieldLabel(Field);
}

public sealed record AuditEventDto(
    string Id,
    DateTimeOffset OccurredAtUtc,
    string? ActorUserId,
    string ActorName,
    string? ActorRole,
    string Action,
    string? EntityType,
    string? EntityId,
    string? OrderId,
    string? CustomerId,
    string Summary,
    IReadOnlyList<AuditChangeDto> Changes,
    string Source,
    string? DeviceId,
    string? IpAddress,
    string? UserAgent)
{
    public string ActionDisplay => AuditActions.Display(Action);

    /// <summary>"22 Oct 2026, 14:05" in Accra time.</summary>
    public string WhenDisplay => Money.DateTime(OccurredAtUtc);

    public string SourceDisplay => Source switch
    {
        "web" => "Web",
        "sync" => "Phone (sync)",
        "api" => "App",
        _ => "System"
    };
}

/// <summary>Activity log filters. Dates are Accra calendar days, inclusive.</summary>
public sealed class AuditQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    [MaxLength(64)] public string? UserId { get; set; }

    /// <summary>Exact action ("order.status_changed") or a group prefix ending with "." ("payment.").</summary>
    [MaxLength(64)] public string? Action { get; set; }

    [MaxLength(32)] public string? EntityType { get; set; }
    [MaxLength(64)] public string? EntityId { get; set; }
    [MaxLength(64)] public string? OrderId { get; set; }
    [MaxLength(64)] public string? CustomerId { get; set; }
    [MaxLength(16)] public string? Source { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }

    /// <summary>Words in the summary (case-insensitive).</summary>
    [MaxLength(100)] public string? Search { get; set; }
}
