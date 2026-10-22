using NeedleBase.Application.Common;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Payments;

/// <summary>
/// Payment entry rules shared by the API and the phone (one source of truth):
/// <list type="bullet">
/// <item>amount &gt; 0, at most 2 decimals, at most <see cref="Payment.AmountMax"/>; method is a known constant;</item>
/// <item>no new payment on a cancelled order (Collected orders still take what's owed);</item>
/// <item>no payment above what's left (entry only; sync stores real offline overpayments — lenient).</item>
/// </list>
/// </summary>
public static class PaymentRules
{
    /// <summary>Sum of non-voided amounts (exact decimal; each amount already has ≤ 2 decimals).</summary>
    public static decimal Paid(IEnumerable<(decimal Amount, bool IsVoided)> payments) =>
        payments.Where(p => !p.IsVoided).Sum(p => p.Amount);

    public static (RecordError Error, string? Detail) CheckEntry(
        string orderStatus, decimal orderTotal, decimal alreadyPaid, decimal amount, string? method, bool lenient)
    {
        if (Payment.CheckAmount(amount) is { } amountError)
        {
            return (RecordError.PaymentInvalid, amountError);
        }

        if (!PaymentMethod.IsValid(method?.Trim()))
        {
            return (RecordError.PaymentInvalid, "Choose how the customer paid.");
        }

        if (lenient)
        {
            return (RecordError.None, null);
        }

        if (orderStatus == OrderStatus.Cancelled)
        {
            return (RecordError.PaymentOrderLocked, null);
        }

        var left = orderTotal - alreadyPaid;
        if (amount > left)
        {
            return (RecordError.PaymentExceedsBalance, left > 0
                ? $"Only {Contracts.Payments.Money.Format(left)} is left to pay on this order."
                : "This order is already paid in full.");
        }

        return (RecordError.None, null);
    }

    /// <summary>Clock skew allowed on a typed/posted payment date (REST refuses later dates; sync dates them "now").</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Earliest believable payment date (older means a broken clock).</summary>
    public static readonly DateTimeOffset EarliestDate = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Sync only: a method this server doesn't know (a newer app) is stored as Other with the original kept in the
    /// note, so real money is never refused for a label. Same mapping on replay, so it doesn't look like a conflict.
    /// </summary>
    public static (string Method, string? Note) NormalizeSyncMethod(string? method, string? note)
    {
        var m = method?.Trim() ?? string.Empty;
        if (PaymentMethod.IsValid(m))
        {
            return (m, string.IsNullOrWhiteSpace(note) ? null : note.Trim());
        }

        var label = m.Length == 0 ? "unknown" : (m.Length > 32 ? m[..32] : m);
        var merged = string.IsNullOrWhiteSpace(note) ? $"[{label}]" : $"{note.Trim()} [{label}]";
        return (PaymentMethod.Other, merged.Length > Domain.Common.FieldLimits.Notes ? merged[..Domain.Common.FieldLimits.Notes] : merged);
    }

    /// <summary>Owner and Manager record/void payments; Tailor can't (roles skill).</summary>
    public static bool CanManage(string? role) => role is BusinessRole.Owner or BusinessRole.Manager;
}
