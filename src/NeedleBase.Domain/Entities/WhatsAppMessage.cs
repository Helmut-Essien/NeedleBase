namespace NeedleBase.Domain.Entities;

/// <summary>
/// One automatic WhatsApp template message for an order status (outbox row + delivery log). Written in the same
/// transaction as the status change, sent later by the background sender, then updated by Meta's delivery webhook.
/// </summary>
public class WhatsAppMessage : Entity, IBusinessScoped
{
    public const int ErrorMax = 300;
    public const int ProviderIdMax = 128;
    public const int MaxAttempts = 5;

    public string BusinessId { get; private set; } = string.Empty;
    public string OrderId { get; private set; } = string.Empty;
    public string CustomerId { get; private set; } = string.Empty;
    public string OrderStatus { get; private set; } = string.Empty;
    public string State { get; private set; } = WhatsAppMessageState.Pending;
    public int Attempts { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset NotBeforeUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    /// <summary>When Meta accepted it (hourly limit counts these; <see cref="UpdatedAtUtc"/> moves with delivery receipts).</summary>
    public DateTimeOffset? SentAtUtc { get; private set; }

    public string? ProviderMessageId { get; private set; }
    public string? Error { get; private set; }

    private WhatsAppMessage()
    {
    }

    public static WhatsAppMessage Create(string id, string businessId, string orderId, string customerId, string status,
        DateTimeOffset now, TimeSpan delay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        return new WhatsAppMessage
        {
            Id = id, BusinessId = businessId, OrderId = orderId, CustomerId = customerId, OrderStatus = status,
            CreatedAtUtc = now, NotBeforeUtc = now + delay, UpdatedAtUtc = now
        };
    }

    public void MarkSent(string providerId, DateTimeOffset now)
    {
        State = WhatsAppMessageState.Sent;
        ProviderMessageId = Cut(providerId, ProviderIdMax);
        Error = null;
        SentAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void Skip(string reason, DateTimeOffset now)
    {
        State = WhatsAppMessageState.Skipped;
        Error = Cut(reason, ErrorMax);
        UpdatedAtUtc = now;
    }

    /// <summary>Counts a failed try: retries later with backoff, or gives up after <see cref="MaxAttempts"/> / a permanent error.</summary>
    public void Fail(string error, bool permanent, DateTimeOffset now)
    {
        Attempts++;
        Error = Cut(error, ErrorMax);
        UpdatedAtUtc = now;
        if (permanent || Attempts >= MaxAttempts)
        {
            State = WhatsAppMessageState.Failed;
            return;
        }

        State = WhatsAppMessageState.Pending;
        NotBeforeUtc = now + Backoff(Attempts);
    }

    /// <summary>1 min, 5 min, 30 min, 2 h.</summary>
    public static TimeSpan Backoff(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromHours(2)
    };

    /// <summary>Delivery webhook: only moves forward (sent → delivered → read); failed always wins.</summary>
    public bool ApplyDelivery(string state, string? error, DateTimeOffset now)
    {
        if (state == WhatsAppMessageState.Failed)
        {
            if (State == WhatsAppMessageState.Failed)
            {
                return false;
            }

            State = state;
            Error = Cut(error ?? "Not delivered", ErrorMax);
            UpdatedAtUtc = now;
            return true;
        }

        if (WhatsAppMessageState.Rank(state) <= WhatsAppMessageState.Rank(State) || State == WhatsAppMessageState.Failed)
        {
            return false;
        }

        State = state;
        UpdatedAtUtc = now;
        return true;
    }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;
}

public static class WhatsAppMessageState
{
    public const string Pending = "Pending";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Delivered = "Delivered";
    public const string Read = "Read";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";

    public static int Rank(string state) => state switch
    {
        Pending => 0,
        Sending => 1,
        Sent => 2,
        Delivered => 3,
        Read => 4,
        _ => -1
    };

    /// <summary>Meta's status names ("sent", "delivered", "read", "failed") to ours; null for unknown.</summary>
    public static string? FromMeta(string? status) => status switch
    {
        "sent" => Sent,
        "delivered" => Delivered,
        "read" => Read,
        "failed" => Failed,
        _ => null
    };
}
