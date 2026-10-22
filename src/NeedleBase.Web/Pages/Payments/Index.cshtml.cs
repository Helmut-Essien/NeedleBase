using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Payments;

/// <summary>Payments list (owner/manager). Read-only on the web in Slice 6; recording stays in the app.</summary>
public class IndexModel : AppPageModel
{
    private readonly INeedleBaseApi _api;

    public IndexModel(INeedleBaseApi api)
    {
        _api = api;
    }

    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PaginatedResponse<PaymentListItemDto>? Result { get; private set; }

    public PagerModel? Pager => Result is null ? null : new PagerModel("/Payments", Result.Page, Result.TotalPages, Result.TotalCount,
        new Dictionary<string, string?>());

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!User.CanSeePayments())
        {
            PageError = "Payments are only shown to the owner and managers.";
            return Page();
        }

        var result = await _api.ListPaymentsAsync(Math.Max(1, PageNumber), PaginationDefaults.DefaultPageSize, cancellationToken);
        if (!result.IsSuccess)
        {
            return Fail(result) ?? Page();
        }

        Result = result.Value;
        Loaded = true;
        return Page();
    }
}
