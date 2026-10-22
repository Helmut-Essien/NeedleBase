using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Dashboard;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages;

/// <summary>Dashboard: workload by status, overdue/due soon, and (owner/manager) money. Independent calls run in parallel.</summary>
public class IndexModel : AppPageModel
{
    private readonly INeedleBaseApi _api;

    public IndexModel(INeedleBaseApi api)
    {
        _api = api;
    }

    public DashboardSummaryDto? Summary { get; private set; }
    public IReadOnlyList<OrderListItemDto> DueNext { get; private set; } = [];
    public IReadOnlyList<PaymentListItemDto> RecentPayments { get; private set; } = [];
    public bool ShowMoney { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ShowMoney = User.CanSeePayments();
        var summaryTask = _api.GetDashboardAsync(cancellationToken);
        var dueTask = _api.ListOrdersAsync(new OrderQuery(PageSize: 8, Status: OrderListSort.OpenFilter, Sort: OrderListSort.Due), cancellationToken);
        var paymentsTask = ShowMoney
            ? Nullable(_api.ListPaymentsAsync(1, 5, cancellationToken))
            : Task.FromResult<ApiResult<PaginatedResponse<PaymentListItemDto>>?>(null);
        await Task.WhenAll(summaryTask, dueTask, paymentsTask);

        var summary = await summaryTask;
        if (!summary.IsSuccess)
        {
            return Fail(summary) ?? Page();
        }

        Summary = summary.Value;
        ShowMoney = ShowMoney && Summary!.Money is not null; // both must agree: the Api decides, the cookie role never widens it
        var due = await dueTask;
        if (due.IsSuccess)
        {
            DueNext = due.Value!.Data;
        }

        var payments = await paymentsTask;
        if (payments is { IsSuccess: true })
        {
            RecentPayments = payments.Value!.Data;
        }

        Loaded = true;
        return Page();
    }

    private static async Task<T?> Nullable<T>(Task<T> task) where T : class => await task;
}
