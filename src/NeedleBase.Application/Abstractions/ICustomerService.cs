using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;

namespace NeedleBase.Application.Abstractions;

public interface ICustomerService
{
    Task<PaginatedResponse<CustomerListItemDto>> ListAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken = default);

    Task<CustomerDetailDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<(CustomerDetailDto? Customer, string? Error)> CreateAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default);

    Task<(CustomerDetailDto? Customer, string? Error)> UpdateAsync(
        string id,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken = default);

    Task<CustomerDeleteResult> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public enum CustomerDeleteResult
{
    Deleted = 0,
    NotFound,

    /// <summary>Customer still has orders (FK restrict). API: 409 CUSTOMER_HAS_ORDERS.</summary>
    HasOrders
}
