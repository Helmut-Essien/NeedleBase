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
    bool IsOverdue)
{
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
    int SortOrder)
{
    public string GarmentDisplay => Domain.Enums.GarmentTemplate.DisplayName(GarmentTemplate);

    /// <summary>Always en-GH ("GH₵ 1,250.00"), whatever the phone's language (XAML StringFormat uses the device culture).</summary>
    public string LineTotalDisplay =>
        $"GH₵ {LineTotal.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-GH"))} · {SnapshotLabel}";

    public string SnapshotLabel =>
        MeasurementFields.Count > 0 || !string.IsNullOrWhiteSpace(MeasurementProfileId)
            ? "Measurements snapped"
            : "No measurements";
}

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
/// One garment line. Line ids are always generated server-side (idempotency is per order id),
/// so a client never supplies them.
/// </summary>
public sealed class CreateOrderLineRequest
{
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
}

public sealed class UpdateOrderStatusRequest
{
    /// <summary>Optional optimistic concurrency: the DateModifiedUtc the client last saw. Stale → 409 STALE_UPDATE.</summary>
    public DateTimeOffset? BaseModifiedUtc { get; set; }

    [Required, MaxLength(FieldLimits.OrderStatus)]
    public string Status { get; set; } = string.Empty;
}
