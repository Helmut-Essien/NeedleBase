using System.ComponentModel.DataAnnotations;
using NeedleBase.Domain.Common;

namespace NeedleBase.Application.Contracts.Sync;

/// <summary>Push operation kinds. Create vs update is decided by <see cref="SyncPushOperation.BaseModifiedUtc"/> (null = create).</summary>
public static class SyncOperation
{
    public const string Upsert = "upsert";
    public const string Delete = "delete";
}

/// <summary>Per-operation push outcome.</summary>
public static class SyncOpStatus
{
    /// <summary>Saved (or already saved: idempotent). Server copy returned; null when deleted.</summary>
    public const string Applied = "applied";

    /// <summary>The server changed (or deleted) the record since the device last saw it. Nothing was overwritten.</summary>
    public const string Conflict = "conflict";

    /// <summary>Rejected by a rule (see <see cref="SyncPushResult.Code"/>). Retrying the same op won't help.</summary>
    public const string Failed = "failed";
}

public sealed record SyncCustomerDto(
    string Id,
    string Name,
    string Phone,
    string? Notes,
    string? PhotoRef,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset DateModifiedUtc);

public sealed record SyncMeasurementDto(
    string Id,
    string CustomerId,
    string GarmentTemplate,
    string Unit,
    string FieldsJson,
    string? Notes,
    DateTimeOffset MeasuredAtUtc,
    DateTimeOffset DateModifiedUtc);

public sealed record SyncOrderLineDto(
    string Id,
    string GarmentTemplate,
    int Quantity,
    decimal UnitPrice,
    string? MeasurementProfileId,
    string Unit,
    string MeasurementFieldsJson,
    string? Notes,
    int SortOrder);

public sealed record SyncOrderDto(
    string Id,
    string CustomerId,
    string DisplayRef,
    string Status,
    DateOnly? DueDate,
    DateOnly? FittingDate,
    string? AssignedToUserId,
    decimal TotalAmount,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset DateModifiedUtc,
    IReadOnlyList<SyncOrderLineDto> Lines);

public sealed record SyncDeletedItemDto(string EntityType, string EntityId, DateTimeOffset DeletedAtUtc);

public sealed class SyncPushOperation
{
    /// <summary>Client queue item id (ULID); echoed in the result.</summary>
    [Required, MaxLength(FieldLimits.Id)]
    public string OpId { get; set; } = string.Empty;

    [Required, MaxLength(FieldLimits.SyncEntityType)]
    public string EntityType { get; set; } = string.Empty;

    [Required, MaxLength(FieldLimits.Id)]
    public string EntityId { get; set; } = string.Empty;

    [Required, MaxLength(FieldLimits.SyncOperation)]
    public string Operation { get; set; } = SyncOperation.Upsert;

    /// <summary>Server <c>DateModifiedUtc</c> the device last saw; null = create.</summary>
    public DateTimeOffset? BaseModifiedUtc { get; set; }

    public SyncCustomerDto? Customer { get; set; }
    public SyncMeasurementDto? Measurement { get; set; }
    public SyncOrderDto? Order { get; set; }
}

public sealed class SyncPushRequest
{
    [Required, MaxLength(FieldLimits.SyncPushBatchMax)]
    public List<SyncPushOperation> Operations { get; set; } = [];
}

public sealed record SyncPushResult(
    string OpId,
    string Status,
    string? Code,
    string? Message,
    SyncCustomerDto? Customer = null,
    SyncMeasurementDto? Measurement = null,
    SyncOrderDto? Order = null);

public sealed record SyncPushResponse(IReadOnlyList<SyncPushResult> Results);

public sealed class SyncPullRequest
{
    /// <summary>Opaque cursor from the previous response; null = everything (fresh device).</summary>
    [MaxLength(2000)]
    public string? Cursor { get; set; }

    [Range(1, FieldLimits.SyncPullLimitMax)]
    public int Limit { get; set; } = 200;
}

/// <summary>
/// One page of changes, in dependency order (customers, then measurements, then orders with lines,
/// then deletes). Keep calling with <see cref="Cursor"/> while <see cref="HasMore"/>; store the last
/// cursor for the next sync. Rows can repeat across syncs (small overlap window): apply as upserts.
/// </summary>
public sealed record SyncPullResponse(
    IReadOnlyList<SyncCustomerDto> Customers,
    IReadOnlyList<SyncMeasurementDto> Measurements,
    IReadOnlyList<SyncOrderDto> Orders,
    IReadOnlyList<SyncDeletedItemDto> Deleted,
    string Cursor,
    bool HasMore,
    DateTimeOffset ServerTimeUtc);

/// <summary>Stable codes in <see cref="SyncPushResult.Code"/> and sync ProblemDetails.</summary>
public static class SyncErrorCodes
{
    public const string CursorInvalid = "SYNC_CURSOR_INVALID";
    public const string PayloadInvalid = "SYNC_PAYLOAD_INVALID";
    public const string StaleBase = "SYNC_CONFLICT";
    public const string Deleted = "SYNC_DELETED_ELSEWHERE";

    /// <summary>The record's customer no longer exists on the server (restore the customer or discard).</summary>
    public const string ParentDeleted = "SYNC_PARENT_DELETED";

    /// <summary>A delete the server refused because the record still has dependents (e.g. a customer with orders).</summary>
    public const string DeleteRefused = "SYNC_DELETE_REFUSED";

    /// <summary>
    /// The server could not store the record (e.g. its id is taken). Deliberately generic: a foreign
    /// business's id gets the same answer as any other unsavable record, so existence is not revealed.
    /// </summary>
    public const string Rejected = "SYNC_REJECTED";
}
