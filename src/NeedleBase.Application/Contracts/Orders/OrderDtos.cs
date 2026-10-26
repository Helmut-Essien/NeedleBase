using System.ComponentModel.DataAnnotations;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Contracts.Orders;

public sealed record OrderListItemDto(
    string Id,
    string DisplayRef,
    string CustomerId,
    string CustomerName,
    string Status,
    DateOnly? DueDate,
    decimal TotalAmount,
    DateTimeOffset DateModifiedUtc,
    bool IsOverdue,
    string? AssignedToUserId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? LocalSyncState = null)
{
    /// <summary>Device only (never sent by the server): this row's queued sync state, see LocalSyncBadge.</summary>
    public string? SyncBadge => NeedleBase.Application.Common.LocalSyncBadge.Text(LocalSyncState);

    public bool HasSyncBadge => LocalSyncState is not null;

    public bool SyncNeedsAttention => LocalSyncState == NeedleBase.Application.Common.LocalSyncBadge.Attention;

    public string StatusDisplay => OrderStatus.DisplayName(Status);

    public string DueDateDisplay => DueDate is { } due
        ? due.ToString("dd MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GH"))
        : "No due date";

    public string TotalDisplay =>
        $"GH₵ {TotalAmount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-GH"))}";

    public string SecondaryLine => $"{DueDateDisplay} · {TotalDisplay}";
}

public sealed record OrderLineDto(
    string Id,
    string GarmentTemplate,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string? MeasurementProfileId,
    string Unit,
    IReadOnlyDictionary<string, decimal?> MeasurementFields,
    string? Notes,
    int SortOrder,
    DateTimeOffset? MeasurementTakenAtUtc = null)
{
    public string GarmentDisplay => Domain.Enums.GarmentTemplate.DisplayName(GarmentTemplate);

    /// <summary>Always en-GH ("GH₵ 1,250.00"), whatever the phone's language (XAML StringFormat uses the device culture).</summary>
    public string LineTotalDisplay =>
        $"GH₵ {LineTotal.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-GH"))} · {SnapshotLabel}";

    public string SnapshotLabel =>
        MeasurementFields.Count > 0 || !string.IsNullOrWhiteSpace(MeasurementProfileId)
            ? "Measurements saved"
            : "No measurements";

    public bool HasMeasurements => MeasurementFields.Any(f => f.Value is not null);

    public bool HasNoMeasurements => !HasMeasurements;

    /// <summary>"Taken 22 Oct 2026" (Accra), or an honest fallback for lines saved before the date was recorded.</summary>
    public string MeasurementTakenDisplay => !HasMeasurements
        ? "No measurements saved with this order"
        : MeasurementTakenAtUtc is { } taken
            ? "Taken " + Payments.Money.Date(taken)
            : "Saved with this order (date not recorded)";

    /// <summary>Snapshot values in display order ("Chest: 102 cm"), nulls skipped.</summary>
    public IReadOnlyList<MeasurementValueDto> MeasurementValues => MeasurementFields
        .Where(f => f.Value is not null)
        .OrderBy(f => FieldRank(f.Key))
        .ThenBy(f => f.Key, StringComparer.Ordinal)
        .Select(f => new MeasurementValueDto(
            Common.GarmentTemplateFields.DisplayLabel(f.Key),
            f.Value!.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " " + Unit))
        .ToList();

    private int FieldRank(string key)
    {
        var fields = Common.GarmentTemplateFields.GetFields(GarmentTemplate);
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i], key, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return int.MaxValue;
    }
}

/// <summary>One read-only snapshot value for display.</summary>
public sealed record MeasurementValueDto(string Label, string Value);

public sealed record OrderDetailDto(
    string Id,
    string BusinessId,
    string DisplayRef,
    string CustomerId,
    string CustomerName,
    string CustomerPhone,
    string Status,
    DateOnly? DueDate,
    DateOnly? FittingDate,
    string? AssignedToUserId,
    decimal TotalAmount,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset DateModifiedUtc,
    bool IsOverdue,
    IReadOnlyList<OrderLineDto> Lines)
{
    public string StatusDisplay => OrderStatus.DisplayName(Status);
}

/// <summary>
/// One garment line. Line ids are generated server-side, except a sync push keeps the device's (valid, unused) line id.
/// On an edit (<see cref="UpdateOrderRequest.Lines"/>) <see cref="Id"/> names an existing line to keep: only its
/// quantity, price and notes change; its garment and measurement snapshot stay frozen.
/// </summary>
public sealed class CreateOrderLineRequest
{
    /// <summary>Edit only: id of the existing line this row keeps. Null/unknown = a new line.</summary>
    [MaxLength(FieldLimits.Id)]
    public string? Id { get; set; }

    /// <summary>When a carried snapshot was taken (sync from devices); profile-backed lines use the profile's date.</summary>
    public DateTimeOffset? MeasurementTakenAtUtc { get; set; }

    [Required, MaxLength(FieldLimits.GarmentTemplate)]
    public string GarmentTemplate { get; set; } = string.Empty;

    [Range(1, FieldLimits.OrderLineQuantityMax)]
    public int Quantity { get; set; } = 1;

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }

    [MaxLength(FieldLimits.Id)]
    public string? MeasurementProfileId { get; set; }

    [Required, MaxLength(FieldLimits.MeasurementUnit)]
    public string Unit { get; set; } = MeasurementUnit.Centimetres;

    /// <summary>Frozen measurement snapshot. Prefer copying from a profile at create time.</summary>
    public Dictionary<string, decimal?> MeasurementFields { get; set; } = new();

    [MaxLength(FieldLimits.Notes)]
    public string? Notes { get; set; }
}

public sealed class CreateOrderRequest
{
    /// <summary>
    /// Set only by sync push (never bound from JSON): skip input rules added after data may already have been
    /// queued on devices (amount/measurement limits, phone format), so legacy data never fails permanently.
    /// Domain invariants still apply.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Lenient { get; set; }

    /// <summary>Optional client ULID for offline idempotency.</summary>
    [MaxLength(FieldLimits.Id)]
    public string? Id { get; set; }

    [Required, MaxLength(FieldLimits.Id)]
    public string CustomerId { get; set; } = string.Empty;

    public DateOnly? DueDate { get; set; }

    public DateOnly? FittingDate { get; set; }

    [MaxLength(FieldLimits.AssignedToUserId)]
    public string? AssignedToUserId { get; set; }

    /// <summary>
    /// Optional client-computed total. The server always computes the total as the sum of
    /// line totals (qty × unit price); if this is sent and differs, the request is rejected
    /// (400 ORDER_TOTAL_MISMATCH). Discounts are planned, not built.
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal? TotalAmount { get; set; }

    [MaxLength(FieldLimits.Notes)]
    public string? Notes { get; set; }

    [Required, MinLength(1)]
    public List<CreateOrderLineRequest> Lines { get; set; } = [];
}

public sealed class UpdateOrderRequest
{
    /// <summary>Optional optimistic concurrency: the DateModifiedUtc the client last saw. Stale → 409 STALE_UPDATE.</summary>
    public DateTimeOffset? BaseModifiedUtc { get; set; }

    public DateOnly? DueDate { get; set; }

    public DateOnly? FittingDate { get; set; }

    [MaxLength(FieldLimits.AssignedToUserId)]
    public string? AssignedToUserId { get; set; }

    [MaxLength(FieldLimits.Notes)]
    public string? Notes { get; set; }

    /// <summary>
    /// Optional garment lines (Owner/Manager). Null = lines unchanged. The full new list: rows with an existing
    /// <see cref="CreateOrderLineRequest.Id"/> are kept (qty/price/notes editable), others are added, missing ones removed.
    /// A new line with no profile and no values snapshots the customer's latest measurements for that garment.
    /// </summary>
    public List<CreateOrderLineRequest>? Lines { get; set; }

    /// <summary>Optional client-computed total; a disagreement is refused (TOTAL_MISMATCH).</summary>
    public decimal? TotalAmount { get; set; }

    /// <summary>Sync push only: skip input limits added after data may already be queued on devices.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Lenient { get; set; }
}

/// <summary>Order list sort keys (query <c>sort</c>).</summary>
public static class OrderListSort
{
    /// <summary>Newest first (default).</summary>
    public const string Newest = "new";

    /// <summary>Earliest due date first, undated last (production board).</summary>
    public const string Due = "due";

    /// <summary>Status filter value meaning "not Collected or Cancelled".</summary>
    public const string OpenFilter = "open";
}

public sealed class UpdateOrderStatusRequest
{
    /// <summary>Optional optimistic concurrency: the DateModifiedUtc the client last saw. Stale → 409 STALE_UPDATE.</summary>
    public DateTimeOffset? BaseModifiedUtc { get; set; }

    [Required, MaxLength(FieldLimits.OrderStatus)]
    public string Status { get; set; } = string.Empty;
}
