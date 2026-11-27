using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Expenses;
using NeedleBase.Application.Licensing;
using NeedleBase.Application.Reports;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Expenses;

/// <summary>
/// Expenses list (expenses plan 7.2): one Accra month (or one order across all months), category filter, totals,
/// paging. Owner/Manager; the Api decides the plan (Starter → upgrade card) and read-only (no Record/Void).
/// </summary>
public class IndexModel : AppPageModel
{
    private readonly INeedleBaseApi _api;

    public IndexModel(INeedleBaseApi api)
    {
        _api = api;
    }

    /// <summary>Accra month <c>yyyy-MM</c>; blank = this month. Ignored when <see cref="OrderId"/> is set.</summary>
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }

    [BindProperty(SupportsGet = true)] public string? Category { get; set; }

    [BindProperty(SupportsGet = true)] public string? OrderId { get; set; }

    /// <summary>"Include voided" (default on: voided rows stay visible, they never count in the totals).</summary>
    [BindProperty(SupportsGet = true, Name = "voided")] public bool IncludeVoided { get; set; } = true;

    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    [TempData] public string? Flash { get; set; }

    /// <summary>Clock for tests.</summary>
    public Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;

    public ExpenseListResponse? Result { get; private set; }
    public DateOnly SelectedMonth { get; private set; }
    public string? FlashMessage { get; private set; }
    public bool NeedsUpgrade { get; private set; }
    public bool IsReadOnly { get; private set; }
    public string? OrderTitle { get; private set; }
    public IReadOnlyList<StaffNameDto> Staff { get; private set; } = Array.Empty<StaffNameDto>();

    public string MonthKey => ReportRules.Key(SelectedMonth);
    public string MonthLabel => ExpenseWeb.MonthLabel(SelectedMonth);
    public string LatestMonthKey => ReportRules.Key(ExpenseWeb.ThisMonth(UtcNow()));
    public string EarliestMonthKey => ReportRules.Key(ReportRules.EarliestMonth);
    public string? PreviousMonthKey => SelectedMonth > ReportRules.EarliestMonth ? ReportRules.Key(SelectedMonth.AddMonths(-1)) : null;
    public string? NextMonthKey => SelectedMonth < ExpenseWeb.ThisMonth(UtcNow()) ? ReportRules.Key(SelectedMonth.AddMonths(1)) : null;
    public bool IsOrderView => !string.IsNullOrWhiteSpace(OrderId);
    public string? CategoryLabel => Category is null ? null : ExpenseCategory.DisplayName(Category);
    public bool CanRecord => Loaded && !IsReadOnly;
    public string? TopCategory => Result?.ByCategory.FirstOrDefault() is { Total: > 0 } top ? $"{top.Label} ({top.TotalDisplay})" : null;

    public string EmptyText => IsOrderView
        ? CategoryLabel is null ? "No expenses for this order yet." : $"No {CategoryLabel.ToLowerInvariant()} expenses for this order."
        : CategoryLabel is null
            ? $"No expenses in {MonthLabel}. Record fabric, staff pay, rent and other costs to see your profit."
            : $"No {CategoryLabel.ToLowerInvariant()} expenses in {MonthLabel}. Choose All to see every expense.";

    public PagerModel? Pager => Result is null ? null : new PagerModel("/Expenses", Result.Items.Page, Result.Items.TotalPages, Result.Items.TotalCount,
        Query(Category));

    /// <summary>Query values for links that keep the page's filters (category replaced by <paramref name="category"/>).</summary>
    public Dictionary<string, string?> Query(string? category) => new()
    {
        ["month"] = IsOrderView ? null : MonthKey,
        ["orderId"] = IsOrderView ? OrderId : null,
        ["category"] = category,
        ["voided"] = IncludeVoided ? null : "false"
    };

    public string FilterUrl(string? category) =>
        "/Expenses?" + string.Join('&', Query(category).Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

    /// <summary>Same filters, another month.</summary>
    public string MonthUrl(string monthKey) =>
        $"/Expenses?month={monthKey}{(Category is null ? "" : "&category=" + Uri.EscapeDataString(Category))}{(IncludeVoided ? "" : "&voided=false")}";

    public string Who(string? userId) => ExpenseWeb.Who(userId, Staff);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        FlashMessage = Flash;
        if (!User.CanSeeExpenses())
        {
            PageError = ExpenseRules.ForbiddenMessage;
            CanRetry = false;
            return Page();
        }

        var today = BusinessTime.Today(UtcNow());
        var month = string.IsNullOrWhiteSpace(Month) ? null : Month.Trim();
        if (!ReportRules.TryResolveMonth(month, today, out var chosen))
        {
            return RedirectToPage("/Expenses/Index"); // like Reports: a bad or future month goes back to this month
        }

        SelectedMonth = chosen;
        Category = ExpenseCategory.IsValid(Category) ? Category : null;
        OrderId = string.IsNullOrWhiteSpace(OrderId) ? null : OrderId.Trim();

        var listTask = _api.GetExpensesAsync(new ExpenseListQuery
        {
            Month = IsOrderView ? null : MonthKey,
            OrderId = OrderId,
            Category = Category,
            IncludeVoided = IncludeVoided,
            Page = Math.Max(1, PageNumber),
            PageSize = PaginationDefaults.DefaultPageSize
        }, cancellationToken);
        var staffTask = _api.GetStaffNamesAsync(cancellationToken);
        var orderTask = IsOrderView ? _api.GetOrderAsync(OrderId!, cancellationToken) : null;
        await Task.WhenAll(new Task?[] { listTask, staffTask, orderTask }.OfType<Task>());

        var result = await listTask;
        if (!result.IsSuccess)
        {
            if (result.Code == RecordErrorMessages.ExpensesNotAllowed)
            {
                NeedsUpgrade = true;
                return Page();
            }

            return Fail(result) ?? Page();
        }

        Result = result.Value;
        if ((await staffTask) is { IsSuccess: true } staff)
        {
            Staff = staff.Value!;
        }

        if (orderTask is not null)
        {
            var order = await orderTask;
            OrderTitle = order.IsSuccess ? $"Order #{order.Value!.DisplayRef} · {order.Value.CustomerName}" : "Order removed";
        }

        IsReadOnly = (await ExpenseWeb.StatusAsync(HttpContext, _api, cancellationToken))?.IsReadOnly == true;
        Loaded = true;
        return Page();
    }
}
