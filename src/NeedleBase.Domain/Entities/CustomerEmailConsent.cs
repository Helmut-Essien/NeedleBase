namespace NeedleBase.Domain.Entities;

/// <summary>
/// A customer's consent to transactional email (order status + payment receipts). Id = CustomerId.
/// Separate from <see cref="WhatsAppConsent"/>. No row, or <see cref="OptedIn"/> false, means no customer emails.
/// </summary>
public class CustomerEmailConsent : Entity, IBusinessScoped
{
    public const int SourceMax = 16;

    public string BusinessId { get; private set; } = string.Empty;
    public string CustomerId { get; private set; } = string.Empty;
    public bool OptedIn { get; private set; }

    /// <summary>"staff" (recorded in the app).</summary>
    public string Source { get; private set; } = "staff";

    public string? ChangedByUserId { get; private set; }
    public DateTimeOffset ChangedAtUtc { get; private set; }

    private CustomerEmailConsent()
    {
    }

    public static CustomerEmailConsent Create(string businessId, string customerId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        return new CustomerEmailConsent
        {
            Id = customerId,
            BusinessId = businessId,
            CustomerId = customerId,
            ChangedAtUtc = now
        };
    }

    public void Set(bool optedIn, string source, string? userId, DateTimeOffset now)
    {
        OptedIn = optedIn;
        Source = source.Length > SourceMax ? source[..SourceMax] : source;
        ChangedByUserId = userId;
        ChangedAtUtc = now;
    }
}
