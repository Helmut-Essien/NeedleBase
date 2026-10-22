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

    /// <summary>
    /// As above, plus <paramref name="sort"/> (<see cref="OrderListSort"/>) and an assignee filter
    /// (<c>"me"</c> = the caller, otherwise a user id).
    /// </summary>
    Task<PaginatedResponse<OrderListItemDto>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? status,
        string? sort,
        string? assignedTo,
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
