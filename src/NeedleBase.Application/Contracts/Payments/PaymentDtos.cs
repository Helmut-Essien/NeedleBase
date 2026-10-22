using System.ComponentModel.DataAnnotations;
using System.Globalization;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Contracts.Payments;

/// <summary>One recorded payment. Amounts are cedi, 2 decimals, formatted en-GH ("GH₵ 1,250.00").</summary>
public sealed record PaymentDto(
    string Id,
    string OrderId,
    string CustomerId,
    decimal Amount,
    string Method,
    string? Note,
    DateTimeOffset RecordedAtUtc,
    bool IsVoided,
    DateTimeOffset? VoidedAtUtc,
    string? VoidReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset DateModifiedUtc)
{
    public string AmountDisplay => Money.Format(Amount);
    public string MethodDisplay => PaymentMethod.DisplayName(Method);
}

/// <summary>An order's money: total, what's been paid (voided payments excluded) and what's left.</summary>
public sealed record OrderPaymentsDto(
    string OrderId,
    string OrderStatus,
    decimal TotalAmount,
    decimal PaidAmount,
    IReadOnlyList<PaymentDto> Payments)
{
    /// <summary>Total − paid. Negative when overpaid (two offline devices both took the last payment).</summary>
    public decimal Balance => TotalAmount - PaidAmount;

    public bool IsOverpaid => Balance < 0;

    public bool IsPaidInFull => Balance <= 0;

    /// <summary>"GH₵ 200.00 of GH₵ 800.00 paid"</summary>
    public string PaidSummary => $"{Money.Format(PaidAmount)} of {Money.Format(TotalAmount)} paid";

    /// <summary>"GH₵ 600.00 left", "Paid in full" or "GH₵ 50.00 overpaid".</summary>
    public string BalanceDisplay => OrderStatus == Domain.Enums.OrderStatus.Cancelled
        ? (PaidAmount > 0 ? $"Order cancelled · {Money.Format(PaidAmount)} recorded" : "Order cancelled")
        : Balance > 0
        ? $"{Money.Format(Balance)} left"
        : Balance == 0 ? "Paid in full" : $"{Money.Format(-Balance)} overpaid";
}

/// <summary>Payments list row (newest first) with the order and customer it belongs to.</summary>
public sealed record PaymentListItemDto(
    string Id,
    string OrderId,
    string OrderDisplayRef,
    string CustomerId,
    string CustomerName,
    decimal Amount,
    string Method,
    DateTimeOffset RecordedAtUtc,
    bool IsVoided,
    DateTimeOffset DateModifiedUtc)
{
    public string AmountDisplay => Money.Format(Amount);
    public string MethodDisplay => PaymentMethod.DisplayName(Method);
    public string RecordedDisplay => Money.Date(RecordedAtUtc);
}

public sealed class RecordPaymentRequest
{
    /// <summary>Client ULID (offline-created payments keep their id; a replay of the same id is idempotent).</summary>
    [MaxLength(FieldLimits.Id)]
    public string? Id { get; set; }

    [Range(typeof(decimal), "0.01", "50000000")]
    public decimal Amount { get; set; }

    [Required, MaxLength(FieldLimits.PaymentMethod)]
    public string Method { get; set; } = PaymentMethod.Cash;

    [MaxLength(FieldLimits.Notes)]
    public string? Note { get; set; }

    /// <summary>When the money was received; defaults to now.</summary>
    public DateTimeOffset? RecordedAtUtc { get; set; }

    /// <summary>
    /// Sync only (never bound from REST input): the money was already taken on a device, so a payment that now
    /// overpays the order, or is on an order cancelled meanwhile, is stored instead of being lost.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Lenient { get; set; }
}

public sealed class VoidPaymentRequest
{
    [MaxLength(FieldLimits.Notes)]
    public string? Reason { get; set; }

    /// <summary>Optional optimistic check: the payment's DateModifiedUtc the caller last saw.</summary>
    public DateTimeOffset? BaseModifiedUtc { get; set; }
}

/// <summary>en-GH money and date formatting (independent of the phone's language).</summary>
public static class Money
{
    private static readonly CultureInfo GhanaCulture = CultureInfo.GetCultureInfo("en-GH");

    public static string Format(decimal amount) => $"GH₵ {amount.ToString("N2", GhanaCulture)}";

    public static string Date(DateTimeOffset utc) =>
        Common.BusinessTime.ToLocal(utc).ToString("dd MMM yyyy", GhanaCulture);
}
