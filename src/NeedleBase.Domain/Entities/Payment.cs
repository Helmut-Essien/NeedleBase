using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Exceptions;

namespace NeedleBase.Domain.Entities;

/// <summary>
/// Money received against an order (deposit or instalment), in cedi. Immutable once recorded, so two devices can
/// never make conflicting edits; a mistake is corrected by voiding it (kept for the record, excluded from balances).
/// Voiding is one-way and idempotent.
/// </summary>
public class Payment : Entity, IBusinessScoped
{
    /// <summary>Largest single payment: the largest possible order (50 lines × GH₵1,000,000).</summary>
    public const decimal AmountMax = 50_000_000m;

    public string BusinessId { get; private set; } = string.Empty;
    public string OrderId { get; private set; } = string.Empty;

    /// <summary>The order's customer (denormalised for lists and receipts).</summary>
    public string CustomerId { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }
    public string Method { get; private set; } = PaymentMethod.Cash;
    public string? Note { get; private set; }

    /// <summary>When the money was received (device clock at recording).</summary>
    public DateTimeOffset RecordedAtUtc { get; private set; }

    public bool IsVoided { get; private set; }
    public DateTimeOffset? VoidedAtUtc { get; private set; }
    public string? VoidReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset DateModifiedUtc { get; private set; }

    private Payment()
    {
    }

    public static Payment Create(
        string id,
        string businessId,
        string orderId,
        string customerId,
        decimal amount,
        string method,
        string? note,
        DateTimeOffset recordedAtUtc,
        DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        if (CheckAmount(amount) is { } amountError)
        {
            throw new DomainRuleException(DomainErrorCodes.PaymentInvalid, amountError);
        }

        method = method?.Trim() ?? string.Empty;
        if (!PaymentMethod.IsValid(method))
        {
            throw new DomainRuleException(DomainErrorCodes.PaymentInvalid, "Choose how the customer paid.");
        }

        return new Payment
        {
            Id = id,
            BusinessId = businessId,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Method = method,
            Note = CleanText(note, nameof(note)),
            RecordedAtUtc = recordedAtUtc,
            CreatedAtUtc = utcNow,
            DateModifiedUtc = utcNow
        };
    }

    /// <summary>Null when valid. Amounts are cedi with at most 2 decimals (never rounded silently).</summary>
    public static string? CheckAmount(decimal amount)
    {
        if (amount <= 0)
        {
            return "Amount must be more than 0.";
        }

        if (amount > AmountMax)
        {
            return $"Amount can be at most GH₵ {AmountMax:N0}.";
        }

        return decimal.Round(amount, 2) != amount ? "Amount can have at most 2 decimal places." : null;
    }

    /// <summary>Marks the payment as recorded by mistake. A second void keeps the first (idempotent).</summary>
    public void Void(string? reason, DateTimeOffset utcNow)
    {
        if (IsVoided)
        {
            return;
        }

        VoidReason = CleanText(reason, nameof(reason));
        IsVoided = true;
        VoidedAtUtc = utcNow;
        DateModifiedUtc = utcNow;
    }

    private static string? CleanText(string? value, string name)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value is { Length: > FieldLimits.Notes })
        {
            throw new ArgumentException($"Note must be at most {FieldLimits.Notes} characters.", name);
        }

        return value;
    }
}
