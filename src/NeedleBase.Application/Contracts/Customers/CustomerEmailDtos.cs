namespace NeedleBase.Application.Contracts.Customers;

public sealed record CustomerEmailConsentDto(
    string CustomerId,
    bool OptedIn,
    string? Source,
    DateTimeOffset? ChangedAtUtc,
    /// <summary>False when the customer has no email on file — opt-in is stored but nothing can send yet.</summary>
    bool HasEmail);

public sealed class SetCustomerEmailConsentRequest
{
    public bool OptedIn { get; set; }

    /// <summary>When the choice was made on the phone (optional; for offline ordering).</summary>
    public DateTimeOffset? ChangedAtUtc { get; set; }

    /// <summary>The phone's clock when it sent this (optional; corrects ChangedAtUtc).</summary>
    public DateTimeOffset? DeviceNowUtc { get; set; }
}
