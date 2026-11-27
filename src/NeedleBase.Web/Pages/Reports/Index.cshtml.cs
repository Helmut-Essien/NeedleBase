using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Reports;
using NeedleBase.Application.Reports;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Reports;

/// <summary>
/// Profit and monthly reports (plan step 7b). Owner/Manager; the Api decides the plan (Starter → upgrade prompt).
/// </summary>
public class IndexModel : AppPageModel
{
    private readonly INeedleBaseApi _api;

    public IndexModel(INeedleBaseApi api)
    {
        _api = api;
    }

    /// <summary>Accra month as <c>yyyy-MM</c>; blank = this month.</summary>
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }

    public MonthlyReportDto? Report { get; private set; }

    /// <summary>The shop's plan doesn't include reports (Starter, trial, unknown plan).</summary>
    public bool NeedsUpgrade { get; private set; }

    /// <summary>Latest Accra month the picker may offer (this month); matches Api <c>REPORT_MONTH_INVALID</c> upper bound.</summary>
    public string LatestMonthKey { get; } =
        ReportRules.Key(ReportRules.FirstOfMonth(BusinessTime.Today(DateTimeOffset.UtcNow)));

    /// <summary>"GH₵ 40.00 more than October 2026", "… less than …", or "Same as …" (money spent vs the month before).</summary>
    public static string SpentChange(MonthlyReportDto r)
    {
        var previous = r.Trend.Count >= 2 ? r.Trend[^2].Label : "last month";
        var diff = r.Spent - r.PreviousSpent;
        return diff switch
        {
            > 0 => $"{NeedleBase.Application.Contracts.Payments.Money.Format(diff)} more than {previous}",
            < 0 => $"{NeedleBase.Application.Contracts.Payments.Money.Format(-diff)} less than {previous}",
            _ => $"Same as {previous}"
        };
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!User.CanSeePayments())
        {
            PageError = ReportRules.ForbiddenMessage;
            CanRetry = false;
            return Page();
        }

        var month = string.IsNullOrWhiteSpace(Month) ? null : Month.Trim();
        var result = await _api.GetMonthlyReportAsync(month, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.Code == ReportRules.NotAllowedCode)
            {
                NeedsUpgrade = true;
                return Page();
            }

            if (result.Code == ReportRules.MonthInvalidCode && month is not null)
            {
                return RedirectToPage("/Reports/Index");
            }

            return Fail(result) ?? Page();
        }

        Report = result.Value;
        Loaded = true;
        return Page();
    }
}
