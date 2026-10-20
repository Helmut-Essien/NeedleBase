using NeedleBase.Application.Contracts.Orders;

namespace NeedleBase.Mobile.Data.Services;

public interface IOrderLocalService
{
    Task<IReadOnlyList<OrderListItemDto>> SearchAsync(
        string businessId,
        string? search,
        string? status,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<OrderDetailDto?> GetByIdAsync(
        string businessId,
        string id,
        CancellationToken cancellationToken = default);

    Task<OrderCountsDto> GetCountsAsync(
        string businessId,
        CancellationToken cancellationToken = default);

    /// <summary>Overdue or Ready orders for Home "Needs attention" (newest first).</summary>
    Task<IReadOnlyList<OrderListItemDto>> ListAttentionAsync(
        string businessId,
        int take = 5,
        CancellationToken cancellationToken = default);

    Task<(OrderDetailDto? Order, string? Error)> SaveAsync(
        string businessId,
        CreateOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<(OrderDetailDto? Order, string? Error)> UpdateStatusAsync(
        string businessId,
        string id,
        string status,
        CancellationToken cancellationToken = default);
}

/// <summary>Home KPI + stage counts from local orders.</summary>
public sealed record OrderCountsDto(
    int ActiveCount,
    int ReadyToCollectCount,
    IReadOnlyDictionary<string, int> ByStatus);
