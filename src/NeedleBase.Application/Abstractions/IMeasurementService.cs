using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Measurements;

namespace NeedleBase.Application.Abstractions;

public interface IMeasurementService
{
    Task<RecordResult<PaginatedResponse<MeasurementListItemDto>>> ListForCustomerAsync(
        string customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<MeasurementDetailDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<RecordResult<MeasurementDetailDto>> CreateAsync(
        string customerId,
        CreateMeasurementRequest request,
        CancellationToken cancellationToken = default);

    Task<RecordResult<MeasurementDetailDto>> UpdateAsync(
        string id,
        UpdateMeasurementRequest request,
        CancellationToken cancellationToken = default);

    Task<RecordError> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
