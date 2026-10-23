using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Audit;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Activity;

/// <summary>
/// Activity log: who did what and when (Owner/Manager). Filters by person, kind, Accra date range, words, and an
/// order or customer (the history links on Order detail / Customers). Server-side paging; staff names load in parallel.
/// </summary>
public class IndexModel : AppPageModel
{
    public const int PageSize = 50;
    private readonly INeedleBaseApi _api;

    public IndexModel(INeedleBaseApi api)
    {
        _api = api;
    }

    [BindProperty(SupportsGet = true, Name = "who")] public string? Who { get; set; }
    [BindProperty(SupportsGet = true, Name = "kind")] public string? Kind { get; set; }
    [BindProperty(SupportsGet = true, Name = "from")] public string? From { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public string? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "order")] public string? OrderId { get; set; }
    [BindProperty(SupportsGet = true, Name = "customer")] public string? CustomerId { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PaginatedResponse<AuditEventDto>? Result { get; private set; }
    public IReadOnlyList<StaffNameDto> Staff { get; private set; } = [];
    public string? DateError { get; private set; }

    /// <summary>Kinds this person may filter by: security events are Owner-only (the Api hides them from Managers).</summary>
    public IReadOnlyList<(string Value, string Label)> Groups =>
        AuditActions.Groups.Where(g => User.Role() == Domain.Enums.BusinessRole.Owner || g.Value != "auth.").ToList();

    public bool Filtered => new[] { Who, Kind, From, To, Search, OrderId, CustomerId }.Any(v => !string.IsNullOrWhiteSpace(v));

    public bool PastEnd => Result is { Data.Count: 0, TotalCount: > 0 };

    public PagerModel? Pager => Result is null ? null : new PagerModel("/Activity", Result.Page, Result.TotalPages, Result.TotalCount, Query());

    public IReadOnlyDictionary<string, string?> Query() => new Dictionary<string, string?>
    {
        ["who"] = Who, ["kind"] = Kind, ["from"] = From, ["to"] = To, ["q"] = Search, ["order"] = OrderId, ["customer"] = CustomerId
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!User.CanSeeActivity())
        {
            PageError = "The activity log is only shown to the owner and managers.";
            CanRetry = false;
            return Page();
        }

        Who = Orders.IndexModel.Normalize(Who, 64);
        OrderId = Orders.IndexModel.Normalize(OrderId, 64);
        CustomerId = Orders.IndexModel.Normalize(CustomerId, 64);
        Search = Orders.IndexModel.Normalize(Search, 100);
        Kind = Groups.Any(g => g.Value == Kind) ? Kind : null;
        var from = ParseDate(From);
        var to = ParseDate(To);
        From = from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        To = to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (from is { } f && to is { } t && f > t)
        {
            DateError = "The start date must be on or before the end date.";
            (from, to) = (to, from); // still show something useful
            (From, To) = (To, From);
        }

        var query = new AuditQuery
        {
            Page = Math.Max(1, PageNumber), PageSize = PageSize, UserId = Who, Action = Kind, From = from, To = to, Search = Search,
            OrderId = OrderId, CustomerId = CustomerId
        };
        var resultTask = _api.ListActivityAsync(query, cancellationToken);
        var staffTask = _api.GetStaffNamesAsync(cancellationToken);
        await Task.WhenAll(resultTask, staffTask);

        var result = await resultTask;
        if (!result.IsSuccess)
        {
            return Fail(result) ?? Page();
        }

        Result = result.Value;
        var staff = await staffTask;
        Staff = staff.IsSuccess ? staff.Value! : [];
        Loaded = true;
        return Page();
    }

    internal static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year is >= 2020 and <= 2100
            ? d
            : null;
}
