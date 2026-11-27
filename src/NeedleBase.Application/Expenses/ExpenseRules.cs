using NeedleBase.Application.Common;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Expenses;

/// <summary>A checked, normalised expense entry (what will be stored).</summary>
public sealed record ExpenseEntry(decimal Amount, string Category, DateOnly SpentOn, string? Note);

/// <summary>
/// Expense entry rules shared by the API, sync and the phone (plans step 7b-2, one source of truth).
/// Strict (REST, phone form): unknown category, future or pre-2020 dates and long notes are refused.
/// Lenient (sync push: the money was already spent on a device): an unknown category becomes Other with the original
/// kept in the note, an impossible date becomes today (Accra), and a long note is cut. A bad amount is refused in both.
/// </summary>
public static class ExpenseRules
{
    public const string NotAllowedMessage =
        "Expenses come with the Workshop and Production plans. Upgrade to see your profit after costs.";

    public const string ForbiddenMessage = "Only the owner or a manager can see or record expenses.";

    public const int VoidReasonMin = 3;

    public const string VoidReasonMessage = "Say why you're voiding it (at least 3 characters).";

    /// <summary>Owner and Manager record, view and void expenses; Tailor can't (same people who see money).</summary>
    public static bool CanManage(string? role) => role is BusinessRole.Owner or BusinessRole.Manager;

    public static (RecordError Error, string? Detail, ExpenseEntry? Entry) Check(
        decimal amount, string? category, DateOnly? spentOn, string? note, DateOnly todayAccra, bool lenient)
    {
        if (MoneyRules.CheckAmount(amount) is { } amountError)
        {
            return (RecordError.ExpenseInvalid, amountError, null);
        }

        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var code = category?.Trim() ?? string.Empty;
        if (!ExpenseCategory.IsValid(code))
        {
            if (!lenient)
            {
                return (RecordError.ExpenseInvalid, "Choose what the money was spent on.", null);
            }

            (code, cleanNote) = NormalizeSyncCategory(code, cleanNote);
        }

        var date = spentOn ?? todayAccra;
        if (Expense.CheckSpentOn(date, todayAccra) is { } dateError)
        {
            if (!lenient)
            {
                return (RecordError.ExpenseInvalid, dateError, null);
            }

            date = todayAccra; // a phone with a wrong clock still spent real money
        }

        if (cleanNote is { Length: > FieldLimits.Notes })
        {
            if (!lenient)
            {
                return (RecordError.ExpenseInvalid, $"Note must be at most {FieldLimits.Notes} characters.", null);
            }

            cleanNote = TextCut.Max(cleanNote, FieldLimits.Notes);
        }

        return (RecordError.None, null, new ExpenseEntry(amount, code, date, cleanNote));
    }

    /// <summary>Strict (REST) needs a reason of at least <see cref="VoidReasonMin"/> characters; sync takes any.</summary>
    public static string? CheckVoidReason(string? reason, bool lenient)
    {
        if (lenient)
        {
            return null;
        }

        return (reason?.Trim().Length ?? 0) < VoidReasonMin ? VoidReasonMessage : null;
    }

    /// <summary>
    /// Sync only: a category this server doesn't know (a newer app) is stored as Other with the original kept in the
    /// note, so real spending is never refused for a label. Same mapping on replay, so it doesn't look like a conflict.
    /// </summary>
    public static (string Category, string? Note) NormalizeSyncCategory(string? category, string? note)
    {
        var c = category?.Trim() ?? string.Empty;
        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (ExpenseCategory.IsValid(c))
        {
            return (c, cleanNote);
        }

        var label = c.Length == 0 ? "unknown" : (c.Length > 32 ? c[..32] : c);
        var merged = cleanNote is null ? $"[{label}]" : $"{cleanNote} [{label}]";
        return (ExpenseCategory.Other, merged.Length > FieldLimits.Notes ? TextCut.Max(merged, FieldLimits.Notes) : merged);
    }
}
