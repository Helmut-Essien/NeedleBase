using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Measurements;

namespace NeedleBase.Application.Abstractions;

public interface IMeasurementService
{
    Task<(PaginatedResponse<MeasurementListItemDto>? Page, string? Error)> ListForCustomerAsync(
        string customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<MeasurementDetailDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<(MeasurementDetailDto? Measurement, string? Error)> CreateAsync(
        string customerId,
        CreateMeasurementRequest request,
        CancellationToken cancellationToken = default);

    Task<(MeasurementDetailDto? Measurement, string? Error)> UpdateAsync(
        string id,
        UpdateMeasurementRequest request,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string? Error)> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
