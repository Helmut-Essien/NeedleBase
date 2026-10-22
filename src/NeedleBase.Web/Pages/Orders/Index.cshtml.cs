using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Orders;

/// <summary>Orders table: server-side search (ref, name, phone keys), status filter and paging.</summary>
public class IndexModel : AppPageModel
{
    private readonly INeedleBaseApi _api;

    public IndexModel(INeedleBaseApi api)
    {
        _api = api;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    /// <summary>"me" = only orders assigned to me (the board's "See all" keeps its filter). Other values are ignored.</summary>
    [BindProperty(SupportsGet = true)] public string? Assigned { get; set; }

    public bool MineOnly => Assigned == "me";

    /// <summary>The requested page is past the last one (e.g. an old link after orders were removed).</summary>
    public bool PastEnd => Result is { Data.Count: 0, TotalCount: > 0 };

    public PaginatedResponse<OrderListItemDto>? Result { get; private set; }

    public static IReadOnlyList<(string Value, string Label)> StatusOptions { get; } =
        new[] { (string.Empty, "All statuses"), (OrderListSort.OpenFilter, "Active (not collected or cancelled)") }
            .Concat(OrderStatus.All.Select(s => (s, OrderStatus.DisplayName(s)))).ToList();

    public PagerModel? Pager => Result is null ? null : new PagerModel("/Orders", Result.Page, Result.TotalPages, Result.TotalCount,
        new Dictionary<string, string?> { ["search"] = Search, ["status"] = Status, ["assigned"] = MineOnly ? "me" : null });

    public bool Filtered => !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(Status) || MineOnly;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Search = Normalize(Search, 100);
        Status = StatusOptions.Any(o => o.Value == Status) ? Status : null; // unknown values are ignored, not sent
        Assigned = MineOnly ? "me" : null;
        var result = await _api.ListOrdersAsync(
            new OrderQuery(Math.Max(1, PageNumber), PaginationDefaults.DefaultPageSize, Search, Status, AssignedTo: Assigned), cancellationToken);
        if (!result.IsSuccess)
        {
            return Fail(result) ?? Page();
        }

        Result = result.Value;
        Loaded = true;
        return Page();
    }

    internal static string? Normalize(string? text, int max)
    {
        var t = text?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
    }
}
