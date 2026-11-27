using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Expenses;

namespace NeedleBase.Mobile.Data.Services;

/// <summary>
/// Expenses on this device (plans step 7b-2). Writes auto-queue for sync; they go up only while the shop's plan
/// includes profit reports (see <see cref="NeedleBase.Mobile.Data.Sync.SyncEngine.ExpensesSyncAllowed"/>).
/// Entry rules = the server's strict ones (<see cref="NeedleBase.Application.Expenses.ExpenseRules"/>).
/// </summary>
public interface IExpenseLocalService
{
    /// <summary>
    /// Records an expense (client ULID; a replay of the same id and content returns the same expense).
    /// Refused while the last pulled plan says expenses aren't included, and while the shop is read-only.
    /// </summary>
    Task<RecordResult<ExpenseDto>> RecordAsync(string businessId, RecordExpenseRequest request, string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>One-way; idempotent (the first reason wins). A reason of at least 3 characters is needed.</summary>
    Task<RecordResult<ExpenseDto>> VoidAsync(string businessId, string expenseId, string? reason, string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same filters and totals as <c>GET /api/expenses</c>: month (blank = this month, or every month when an order is
    /// given), category, order, voided listed or not (never counted). Newest first.
    /// </summary>
    Task<RecordResult<LocalExpenseList>> ListAsync(string businessId, ExpenseListQuery query, CancellationToken cancellationToken = default);

    Task<LocalExpense?> GetAsync(string businessId, string expenseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// This phone's user can't see expenses (role is Tailor): delete synced copies that have no queued change, and ask
    /// for a full resync so a later promotion pulls them again. Returns how many were removed.
    /// </summary>
    Task<int> PurgeHiddenAsync(string businessId, CancellationToken cancellationToken = default);
}

/// <summary>An expense plus whether it's still only on this device (queued for sync).</summary>
public sealed record LocalExpense(ExpenseDto Expense, bool IsPending)
{
    public string Id => Expense.Id;
    public bool IsVoided => Expense.IsVoided;

    /// <summary>Linked to an order this phone no longer has, or the server unlinked it because the order was deleted.</summary>
    public bool IsOrderRemoved =>
        (Expense.OrderId is not null && Expense.OrderDisplayRef is null) ||
        (Expense.OrderId is null && Expense.Note?.EndsWith(NeedleBase.Domain.Entities.Expense.OrderRemovedMarker, StringComparison.Ordinal) == true);

    /// <summary>"Order #0042 · Efua Mensah", "Order removed", or null when not linked.</summary>
    public string? OrderLine => IsOrderRemoved ? "Order removed"
        : Expense.OrderDisplayRef is { } displayRef ? $"Order #{displayRef} · {Expense.CustomerName}"
        : null;

    /// <summary>Badge text, or null: "Recorded on this device" until the server has it.</summary>
    public string? StatusBadge => Expense.IsVoided ? "Voided" : IsPending ? "Recorded on this device" : null;

    public bool HasStatusBadge => StatusBadge is not null;
}

/// <summary>Rows plus totals over the whole filter (voided never counted). <see cref="Month"/> is yyyy-MM or null (one order, all months).</summary>
public sealed record LocalExpenseList(
    IReadOnlyList<LocalExpense> Items,
    decimal Total,
    int Count,
    IReadOnlyList<CategoryTotalDto> ByCategory,
    string? Month)
{
    public string TotalDisplay => NeedleBase.Application.Contracts.Payments.Money.Format(Total);
    public bool HasPending => Items.Any(i => i.IsPending);
}
