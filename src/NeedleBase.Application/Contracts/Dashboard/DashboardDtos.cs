using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Contracts.Dashboard;

/// <summary>Orders in one status (every status is listed, zero included, in production order).</summary>
public sealed record StatusCountDto(string Status, int Count)
{
    public string StatusDisplay => OrderStatus.DisplayName(Status);
}

/// <summary>
/// Money totals for the owner/manager dashboard (null for tailors). Outstanding = what's left on open orders
/// (not Cancelled), never negative per order; received = non-voided payments by their Accra recorded date.
/// </summary>
public sealed record DashboardMoneyDto(decimal Outstanding, int OrdersWithBalance, decimal ReceivedToday, decimal ReceivedLast7Days)
{
    public string OutstandingDisplay => Money.Format(Outstanding);
    public string ReceivedTodayDisplay => Money.Format(ReceivedToday);
    public string ReceivedLast7DaysDisplay => Money.Format(ReceivedLast7Days);
}

/// <summary>Dashboard counts, computed by the server in a few aggregate queries (never download-all).</summary>
public sealed record DashboardSummaryDto(
    int ActiveOrders,
    int Overdue,
    int DueNext7Days,
    int FittingsNext7Days,
    IReadOnlyList<StatusCountDto> ByStatus,
    DashboardMoneyDto? Money,
    DateOnly Today);
