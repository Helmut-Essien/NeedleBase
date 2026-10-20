using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Customers;

namespace NeedleBase.Mobile.Data.Services;

public interface ICustomerLocalService
{
    Task<IReadOnlyList<CustomerListItemDto>> SearchAsync(
        string businessId,
        string? search,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<CustomerDetailDto?> GetByIdAsync(
        string businessId,
        string id,
        CancellationToken cancellationToken = default);

    Task<(CustomerDetailDto? Customer, string? Error)> SaveAsync(
        string businessId,
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default);

    Task<(CustomerDetailDto? Customer, string? Error)> UpdateAsync(
        string businessId,
        string id,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a local customer (and its measurements). Refuses with <see cref="CustomerDeleteResult.HasOrders"/>
    /// when orders reference the customer (same rule as the API's 409 CUSTOMER_HAS_ORDERS).
    /// </summary>
    Task<CustomerDeleteResult> DeleteAsync(
        string businessId,
        string id,
        CancellationToken cancellationToken = default);
}
