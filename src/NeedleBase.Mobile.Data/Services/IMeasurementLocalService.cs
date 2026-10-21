using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Measurements;

namespace NeedleBase.Mobile.Data.Services;

public interface IMeasurementLocalService
{
    Task<IReadOnlyList<MeasurementListItemDto>> ListForCustomerAsync(
        string businessId,
        string customerId,
        CancellationToken cancellationToken = default);

    Task<MeasurementDetailDto?> GetByIdAsync(
        string businessId,
        string id,
        CancellationToken cancellationToken = default);

    Task<RecordResult<MeasurementDetailDto>> SaveAsync(
        string businessId,
        string customerId,
        CreateMeasurementRequest request,
        CancellationToken cancellationToken = default);
}
