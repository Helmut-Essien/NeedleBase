namespace NeedleBase.Domain.Entities;

/// <summary>
/// Dedupe log for customer transactional emails (status or receipt). Written in the same SaveChanges as the
/// order/payment change so sync replays do not double-send.
/// </summary>
public class CustomerEmailSent : Entity, IBusinessScoped
{
    public const string KindStatus = "status";
    public const string KindReceipt = "receipt";

    public string BusinessId { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;

    /// <summary>Order id (status) or payment id (receipt).</summary>
    public string RefId { get; private set; } = string.Empty;

    /// <summary>Order status for <see cref="KindStatus"/>; empty for receipts.</summary>
    public string StatusKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private CustomerEmailSent()
    {
    }

    public static CustomerEmailSent ForStatus(
        string id, string businessId, string orderId, string status, DateTimeOffset now) =>
        new()
        {
            Id = id,
            BusinessId = businessId,
            Kind = KindStatus,
            RefId = orderId,
            StatusKey = status,
            CreatedAtUtc = now
        };

    public static CustomerEmailSent ForReceipt(
        string id, string businessId, string paymentId, DateTimeOffset now) =>
        new()
        {
            Id = id,
            BusinessId = businessId,
            Kind = KindReceipt,
            RefId = paymentId,
            StatusKey = string.Empty,
            CreatedAtUtc = now
        };
}
