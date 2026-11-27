using System.Text;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Exceptions;

namespace NeedleBase.Domain.Entities;

/// <summary>
/// Money the shop paid out (fabric, staff pay, rent, repairs, transport, other), in cedi, optionally for one order.
/// Append-only like <see cref="Payment"/>: never edited, so two devices can't make conflicting changes; a mistake is
/// voided (kept for the record, excluded from totals and profit). Voiding is one-way and idempotent.
/// </summary>
public class Expense : Entity, IBusinessScoped
{
    public const decimal AmountMax = MoneyRules.AmountMax;

    /// <summary>Earliest date an expense can carry (same floor as monthly reports).</summary>
    public static readonly DateOnly EarliestSpentOn = new(2020, 1, 1);

    /// <summary>Appended to the note when the linked order no longer exists on the server.</summary>
    public const string OrderRemovedMarker = "[order removed]";

    public string BusinessId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Category { get; private set; } = ExpenseCategory.Other;

    /// <summary>The Accra calendar date the money left the shop (a date, not an instant, so months group exactly).</summary>
    public DateOnly SpentOn { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Optional order of the same shop this cost belongs to; null for shop-wide costs.</summary>
    public string? OrderId { get; private set; }

    /// <summary>Who recorded it (set by the server from the caller).</summary>
    public string? RecordedByUserId { get; private set; }

    public bool IsVoided { get; private set; }
    public DateTimeOffset? VoidedAtUtc { get; private set; }
    public string? VoidedByUserId { get; private set; }
    public string? VoidReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset DateModifiedUtc { get; private set; }

    private Expense()
    {
    }

    /// <param name="todayAccra">Today's Accra calendar date; an expense can't be dated after it.</param>
    public static Expense Create(
        string id,
        string businessId,
        decimal amount,
        string category,
        DateOnly spentOn,
        string? note,
        string? orderId,
        string? recordedByUserId,
        DateOnly todayAccra,
        DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        if (MoneyRules.CheckAmount(amount) is { } amountError)
        {
            throw new DomainRuleException(DomainErrorCodes.ExpenseInvalid, amountError);
        }

        category = category?.Trim() ?? string.Empty;
        if (!ExpenseCategory.IsValid(category))
        {
            throw new DomainRuleException(DomainErrorCodes.ExpenseInvalid, "Choose what the money was spent on.");
        }

        if (CheckSpentOn(spentOn, todayAccra) is { } dateError)
        {
            throw new DomainRuleException(DomainErrorCodes.ExpenseInvalid, dateError);
        }

        orderId = string.IsNullOrWhiteSpace(orderId) ? null : orderId.Trim();
        if (orderId is { Length: > FieldLimits.Id })
        {
            throw new DomainRuleException(DomainErrorCodes.ExpenseInvalid, "That order can't be linked.");
        }

        return new Expense
        {
            Id = id,
            BusinessId = businessId,
            Amount = amount,
            Category = category,
            SpentOn = spentOn,
            Note = CleanText(note, nameof(note)),
            OrderId = orderId,
            RecordedByUserId = CleanUserId(recordedByUserId, nameof(recordedByUserId)),
            CreatedAtUtc = utcNow,
            DateModifiedUtc = utcNow
        };
    }

    /// <summary>Null when valid: not before <see cref="EarliestSpentOn"/> and not after today (Accra).</summary>
    public static string? CheckSpentOn(DateOnly spentOn, DateOnly todayAccra)
    {
        if (spentOn < EarliestSpentOn)
        {
            return "The date can't be before 01 Jan 2020.";
        }

        return spentOn > todayAccra ? "The date can't be in the future." : null;
    }

    /// <summary>Marks the expense as recorded by mistake. A second void keeps the first (idempotent; first reason wins).</summary>
    public void Void(string? reason, string? voidedByUserId, DateTimeOffset utcNow)
    {
        if (IsVoided)
        {
            return;
        }

        VoidReason = CleanText(reason, nameof(reason));
        VoidedByUserId = CleanUserId(voidedByUserId, nameof(voidedByUserId));
        IsVoided = true;
        VoidedAtUtc = utcNow;
        DateModifiedUtc = utcNow;
    }

    /// <summary>
    /// Drops the order link when the order no longer exists (sync only), keeping the money record and noting why.
    /// No change when there is no link.
    /// </summary>
    public void DetachOrder(DateTimeOffset utcNow)
    {
        if (OrderId is null)
        {
            return;
        }

        OrderId = null;
        var note = Note is null ? OrderRemovedMarker : Note + " " + OrderRemovedMarker;
        if (note.Length > FieldLimits.Notes)
        {
            note = TextCut.Max(Note!, FieldLimits.Notes - OrderRemovedMarker.Length - 1).TrimEnd() + " " + OrderRemovedMarker;
        }

        Note = note;
        DateModifiedUtc = utcNow;
    }

    /// <summary>Trims, removes control characters (keeps line breaks and tabs); blank → null; enforces the note limit.</summary>
    private static string? CleanText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsControl(c) || c is '\n' or '\r' or '\t')
            {
                builder.Append(c);
            }
        }

        var cleaned = builder.ToString().Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }

        if (cleaned.Length > FieldLimits.Notes)
        {
            throw new DomainRuleException(
                DomainErrorCodes.ExpenseInvalid, $"Note must be at most {FieldLimits.Notes} characters.");
        }

        return cleaned;
    }

    private static string? CleanUserId(string? value, string name)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value is { Length: > FieldLimits.AssignedToUserId })
        {
            throw new ArgumentException($"User id must be at most {FieldLimits.AssignedToUserId} characters.", name);
        }

        return value;
    }
}
