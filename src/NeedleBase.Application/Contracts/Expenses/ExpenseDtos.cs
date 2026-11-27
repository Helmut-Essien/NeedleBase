using System.ComponentModel.DataAnnotations;
using System.Globalization;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Contracts.Expenses;

/// <summary>One expense (money paid out). Amounts are cedi, 2 decimals; <see cref="SpentOn"/> is an Accra date.</summary>
public sealed record ExpenseDto(
    string Id,
    decimal Amount,
    string Category,
    DateOnly SpentOn,
    string? Note,
    string? OrderId,
    string? OrderDisplayRef,
    string? CustomerName,
    string? RecordedByUserId,
    bool IsVoided,
    DateTimeOffset? VoidedAtUtc,
    string? VoidedByUserId,
    string? VoidReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset DateModifiedUtc)
{
    public string AmountDisplay => Money.Format(Amount);
    public string CategoryDisplay => ExpenseCategory.DisplayName(Category);
    public string SpentOnDisplay => ExpenseFormat.Date(SpentOn);
}

/// <summary>Non-voided total for one category.</summary>
public sealed record CategoryTotalDto(string Category, decimal Total, int Count)
{
    public string Label => ExpenseCategory.DisplayName(Category);
    public string TotalDisplay => Money.Format(Total);
}

/// <summary>
/// A page of expenses plus totals over the whole filter (voided excluded from totals, even when listed).
/// <see cref="Month"/> is <c>yyyy-MM</c>, or null when listing one order across all months.
/// </summary>
public sealed record ExpenseListResponse(
    PaginatedResponse<ExpenseDto> Items,
    decimal Total,
    int Count,
    IReadOnlyList<CategoryTotalDto> ByCategory,
    string? Month)
{
    public string TotalDisplay => Money.Format(Total);
}

/// <summary>Filters for <c>GET /api/expenses</c>.</summary>
public sealed class ExpenseListQuery
{
    /// <summary><c>yyyy-MM</c> Accra month; blank = this month (or every month when <see cref="OrderId"/> is set).</summary>
    public string? Month { get; set; }

    public string? Category { get; set; }

    public string? OrderId { get; set; }

    public bool IncludeVoided { get; set; } = true;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = PaginationDefaults.DefaultPageSize;
}

public sealed class RecordExpenseRequest
{
    /// <summary>Client ULID (offline-created expenses keep their id; a replay of the same id is idempotent).</summary>
    [MaxLength(FieldLimits.Id)]
    public string? Id { get; set; }

    [Range(typeof(decimal), "0.01", "50000000")]
    public decimal Amount { get; set; }

    [Required, MaxLength(FieldLimits.ExpenseCategory)]
    public string Category { get; set; } = ExpenseCategory.Other;

    /// <summary>Accra date the money was paid; defaults to today.</summary>
    public DateOnly? SpentOn { get; set; }

    [MaxLength(FieldLimits.Notes)]
    public string? Note { get; set; }

    /// <summary>Optional order of the same shop.</summary>
    [MaxLength(FieldLimits.Id)]
    public string? OrderId { get; set; }

    /// <summary>Sync only (never bound from REST input): keep spending already made on a device (see ExpenseRules).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Lenient { get; set; }
}

public sealed class VoidExpenseRequest
{
    [MaxLength(FieldLimits.Notes)]
    public string? Reason { get; set; }

    /// <summary>Optional optimistic check: the expense's DateModifiedUtc the caller last saw.</summary>
    public DateTimeOffset? BaseModifiedUtc { get; set; }

    /// <summary>Sync only: any reason is accepted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Lenient { get; set; }
}

/// <summary>Outcome of a record call: the stored expense and whether this call created it (false = idempotent replay).</summary>
public sealed record ExpenseSaveResult(ExpenseDto Expense, bool Created);

public static class ExpenseFormat
{
    private static readonly CultureInfo GhanaCulture = CultureInfo.GetCultureInfo("en-GH");

    /// <summary>"27 Nov 2026".</summary>
    public static string Date(DateOnly date) => date.ToString("dd MMM yyyy", GhanaCulture);
}
