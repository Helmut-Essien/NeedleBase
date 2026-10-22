using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Payments;

namespace NeedleBase.Application.Payments;

public interface IPaymentService
{
    /// <summary>The order's payments (newest first) with paid/balance. Null when the order isn't in this business.</summary>
    Task<RecordResult<OrderPaymentsDto>> GetForOrderAsync(string orderId, CancellationToken cancellationToken = default);

    Task<RecordResult<PaginatedResponse<PaymentListItemDto>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<RecordResult<PaymentDto>> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Records a payment. Same id + same content = idempotent replay; same id + different content = conflict.</summary>
    Task<RecordResult<PaymentDto>> RecordAsync(string orderId, RecordPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Voids (reverses) a payment. Idempotent.</summary>
    Task<RecordResult<PaymentDto>> VoidAsync(string id, VoidPaymentRequest request, CancellationToken cancellationToken = default);
}
