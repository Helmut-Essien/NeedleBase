using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Expenses;
using NeedleBase.Application.Reports;

namespace NeedleBase.Web.Services;

/// <summary>Shared bits for the expense pages (expenses plan section 7). The Api decides; this only shapes the UI.</summary>
public static class ExpenseWeb
{
    public const string FlashRecorded = "Expense recorded.";
    public const string FlashVoided = "Expense voided. It stays in your records and no longer counts toward profit.";
    public const string UpgradeTitle = "Expenses aren't on your plan";
    public const string UpgradeBody = "Record what the shop pays out (fabric, staff pay, rent and bills) and see your profit each month.";

    /// <summary>Owner/Manager see expenses (Tailors never do; the Api also returns 403 EXPENSES_FORBIDDEN).</summary>
    public static bool CanSeeExpenses(this ClaimsPrincipal user) => ExpenseRules.CanManage(user.Role());

    /// <summary>The shop's licence status (cached a minute per shop, shared with the banner); null when unknown.</summary>
    public static Task<SubscriptionStatusDto?> StatusAsync(HttpContext http, INeedleBaseApi api, CancellationToken ct)
    {
        var cache = http.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
        return cache is null
            ? FetchAsync(api, ct)
            : SubscriptionBannerViewComponent.GetAsync(api, cache, http.User, ct);
    }

    private static async Task<SubscriptionStatusDto?> FetchAsync(INeedleBaseApi api, CancellationToken ct)
    {
        var result = await api.GetSubscriptionAsync(ct);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>"Ama Owusu", "Former team member" (left the shop), or "Not recorded" (older phone rows).</summary>
    public static string Who(string? userId, IReadOnlyList<StaffNameDto> staff) =>
        userId is null ? "Not recorded"
        : staff.FirstOrDefault(s => s.UserId == userId)?.Name is { Length: > 0 } name ? name
        : "Former team member";

    /// <summary>"#ORD-12 · Ama Mensah", "Order removed" (no longer in the shop), or null when not linked.</summary>
    public static string? OrderLine(ExpenseDto e) =>
        e.OrderId is null ? null
        : e.OrderDisplayRef is null ? "Order removed"
        : $"#{e.OrderDisplayRef}{(string.IsNullOrWhiteSpace(e.CustomerName) ? "" : " · " + e.CustomerName)}";

    /// <summary>First line of the note, shortened for a table cell.</summary>
    public static string? NoteLine(string? note, int max = 80)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var line = note.Trim().Split('\n')[0].Trim();
        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
    }

    public static string CountText(int count) => count == 1 ? "1 expense" : $"{count} expenses";

    public static string MonthLabel(DateOnly month) => ReportRules.Label(month);

    /// <summary>This Accra month (the latest a picker may offer).</summary>
    public static DateOnly ThisMonth(DateTimeOffset utcNow) => ReportRules.FirstOfMonth(BusinessTime.Today(utcNow));
}
