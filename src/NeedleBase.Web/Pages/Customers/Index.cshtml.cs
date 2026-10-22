using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Customers;

/// <summary>Customers table: the Api's server-side search keys (accent-folded name, phone digits in any format), paging.</summary>
public class IndexModel : AppPageModel
{
    private readonly INeedleBaseApi _api;

    public IndexModel(INeedleBaseApi api)
    {
        _api = api;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PaginatedResponse<CustomerListItemDto>? Result { get; private set; }

    public PagerModel? Pager => Result is null ? null : new PagerModel("/Customers", Result.Page, Result.TotalPages, Result.TotalCount,
        new Dictionary<string, string?> { ["search"] = Search });

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Search = Orders.IndexModel.Normalize(Search, 100);
        var result = await _api.ListCustomersAsync(Search, Math.Max(1, PageNumber), PaginationDefaults.DefaultPageSize, cancellationToken);
        if (!result.IsSuccess)
        {
            return Fail(result) ?? Page();
        }

        Result = result.Value;
        Loaded = true;
        return Page();
    }
}
