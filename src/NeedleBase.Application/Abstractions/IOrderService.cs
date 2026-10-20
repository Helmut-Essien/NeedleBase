using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Orders;

namespace NeedleBase.Application.Abstractions;

public interface IOrderService
{
    Task<PaginatedResponse<OrderListItemDto>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? status,
        CancellationToken cancellationToken = default);

    Task<OrderDetailDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<OrderResult> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<OrderResult> UpdateAsync(
        string id,
        UpdateOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<OrderResult> UpdateStatusAsync(
        string id,
        UpdateOrderStatusRequest request,
        CancellationToken cancellationToken = default);

    Task<OrderError> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default);
}
