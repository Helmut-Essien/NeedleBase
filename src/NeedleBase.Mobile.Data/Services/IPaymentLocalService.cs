using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Payments;

namespace NeedleBase.Mobile.Data.Services;

/// <summary>Payments on this device (Slice 5). Writes auto-queue for sync; amounts in cedi, en-GH display.</summary>
public interface IPaymentLocalService
{
    /// <summary>The order's money (null when the order isn't on this device).</summary>
    Task<LocalOrderPayments?> GetForOrderAsync(string businessId, string orderId, CancellationToken cancellationToken = default);

    /// <summary>Newest first, voided included (shown struck through).</summary>
    Task<IReadOnlyList<LocalPaymentListItem>> ListAsync(string businessId, int take = 100, CancellationToken cancellationToken = default);

    Task<LocalPaymentReceipt?> GetReceiptAsync(string businessId, string paymentId, CancellationToken cancellationToken = default);

    /// <summary>Entry rules = the server's (<see cref="NeedleBase.Application.Payments.PaymentRules"/>): no overpay, not on a cancelled order.</summary>
    Task<RecordResult<PaymentDto>> RecordAsync(string businessId, string orderId, RecordPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// This phone's user can't see payments any more (role is Tailor): delete synced payment copies that have no
    /// queued change, and ask for a full resync so a later promotion pulls them again. Returns how many were removed.
    /// </summary>
    Task<int> PurgeHiddenAsync(string businessId, CancellationToken cancellationToken = default);

    Task<RecordResult<PaymentDto>> VoidAsync(string businessId, string paymentId, string? reason, CancellationToken cancellationToken = default);
}

/// <summary>A payment plus whether it's still only on this device (queued for sync).</summary>
public sealed record LocalPayment(PaymentDto Payment, bool IsPending)
{
    public string Id => Payment.Id;
    public string AmountDisplay => Payment.AmountDisplay;

    /// <summary>"MoMo payment recorded" — never "paid via MoMo" (NeedleBase doesn't confirm with the provider).</summary>
    public string MethodLine => $"{Payment.Method} payment recorded · {Money.Date(Payment.RecordedAtUtc)}";

    /// <summary>Badge text, or null: "Recorded on this device" until the server has it.</summary>
    public string? StatusBadge => Payment.IsVoided ? "Voided" : IsPending ? "Recorded on this device" : null;

    public bool HasStatusBadge => StatusBadge is not null;
    public bool IsVoided => Payment.IsVoided;
}

public sealed record LocalOrderPayments(OrderPaymentsDto Summary, IReadOnlyList<LocalPayment> Payments)
{
    public bool HasPayments => Payments.Count > 0;
    public bool HasPending => Payments.Any(p => p.IsPending);

    /// <summary>Owner/manager can add one: not cancelled and something is still owed.</summary>
    public bool CanRecord => Summary.OrderStatus != NeedleBase.Domain.Enums.OrderStatus.Cancelled && Summary.Balance > 0;
}

public sealed record LocalPaymentListItem(PaymentListItemDto Item, bool IsPending)
{
    public string Id => Item.Id;
    public string Title => $"{Item.AmountDisplay} · {Item.CustomerName}";
    public string Subtitle => $"Order #{Item.OrderDisplayRef} · {Item.Method} · {Item.RecordedDisplay}";
    public string? StatusBadge => Item.IsVoided ? "Voided" : IsPending ? "Recorded on this device" : null;
    public bool HasStatusBadge => StatusBadge is not null;
    public bool IsVoided => Item.IsVoided;
}

/// <summary>Everything a receipt shows: business header, customer, order totals, this payment.</summary>
public sealed record LocalPaymentReceipt(
    LocalPayment Payment,
    string OrderId,
    string OrderDisplayRef,
    string CustomerName,
    string CustomerPhone,
    OrderPaymentsDto Totals)
{
    /// <summary>Plain-text receipt for the share sheet (WhatsApp/SMS). Business name is added by the app.</summary>
    public string ToShareText(string businessName)
    {
        var p = Payment.Payment;
        var lines = new List<string>
        {
            businessName,
            "Payment receipt",
            string.Empty,
            $"Customer: {CustomerName}",
            $"Order #{OrderDisplayRef}",
            $"Date: {Money.Date(p.RecordedAtUtc)}",
            $"Amount: {p.AmountDisplay}",
            $"Method: {p.MethodDisplay}",
        };
        // The note is an internal record (references, staff remarks): it never leaves the phone on a receipt.
        if (p.IsVoided)
        {
            lines.Add("This payment was voided.");
        }

        lines.Add(string.Empty);
        lines.Add($"Order total: {Money.Format(Totals.TotalAmount)}");
        lines.Add($"Paid so far: {Money.Format(Totals.PaidAmount)}");
        lines.Add(Totals.Balance > 0 ? $"Balance: {Money.Format(Totals.Balance)}" : Totals.BalanceDisplay);
        if (Payment.IsPending)
        {
            lines.Add(string.Empty);
            lines.Add("Recorded on this device; not yet synced.");
        }

        return string.Join('\n', lines);
    }
}
