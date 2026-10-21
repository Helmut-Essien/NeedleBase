using NeedleBase.Application.Common;
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

    /// <summary>Customers for an empty picker: newest orders first, then newest added.</summary>
    Task<IReadOnlyList<CustomerListItemDto>> RecentAsync(
        string businessId,
        int take = 8,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerListItemDto>> FindByPhoneAsync(
        string businessId,
        string? phone,
        string? excludeId,
        CancellationToken cancellationToken = default);

    Task<CustomerDetailDto?> GetByIdAsync(
        string businessId,
        string id,
        CancellationToken cancellationToken = default);

    Task<RecordResult<CustomerDetailDto>> SaveAsync(
        string businessId,
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default);

    Task<RecordResult<CustomerDetailDto>> UpdateAsync(
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
