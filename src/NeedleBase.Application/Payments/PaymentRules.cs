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

    /// <summary>Owner and Manager record/void payments; Tailor can't (roles skill).</summary>
    public static bool CanManage(string? role) => role is BusinessRole.Owner or BusinessRole.Manager;
}
