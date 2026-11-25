using System.ComponentModel.DataAnnotations;

namespace NeedleBase.Application.Contracts.Licensing;

/// <summary>
/// The shop's license as everyone in it sees it (web layout, phone banner via sync pull, <c>GET /api/subscription</c>).
/// Never contains the key or the token.
/// </summary>
public sealed record SubscriptionStatusDto
{
    /// <summary>See <see cref="NeedleBase.Application.Licensing.SubscriptionStates"/>.</summary>
    public string State { get; init; } = NeedleBase.Application.Licensing.SubscriptionStates.NotAttached;

    /// <summary>New work is paused (view and sync of earlier offline changes still work).</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Banner text, or null for no banner.</summary>
    public string? Message { get; init; }

    /// <summary>The banner is an Owner task (attach a key) and is not shown to other staff.</summary>
    public bool OwnerOnly { get; init; }

    /// <summary>Raw plan string from Platform (may be blank or unknown).</summary>
    public string? Plan { get; init; }

    /// <summary>Resolved plan code from <c>PlanCatalog</c> (<c>starter</c>, <c>workshop</c>, <c>production</c>, or <c>unlimited</c>).</summary>
    public string? PlanCode { get; init; }

    /// <summary>Plain-language plan name for the License page (e.g. Starter).</summary>
    public string? PlanDisplayName { get; init; }

    public bool IsTrial { get; init; }
    public bool IsComplimentary { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>When new work pauses if nothing changes (only while the license has a problem).</summary>
    public DateTimeOffset? ReadOnlyFromUtc { get; init; }

    public string? KeyHint { get; init; }
    public DateTimeOffset? LastCheckedUtc { get; init; }

    /// <summary>Max staff excluding the Owner; null when unlimited.</summary>
    public int? MaxStaffExcludingOwner { get; init; }

    /// <summary>Active staff excluding the Owner.</summary>
    public int StaffUsedExcludingOwner { get; init; }

    /// <summary>Open (unexpired, unrevoked) invites that reserve staff seats.</summary>
    public int PendingInviteCount { get; init; }

    /// <summary>Paid / keyless / complimentary shops may export (including while read-only).</summary>
    public bool CanExport { get; init; }

    /// <summary>Whether receipts should show the shop phone number.</summary>
    public bool ShopPhoneOnReceipts { get; init; }

    public static SubscriptionStatusDto Unlocked { get; } = new()
    {
        State = NeedleBase.Application.Licensing.SubscriptionStates.Active,
        PlanCode = NeedleBase.Application.Licensing.PlanCodes.Unlimited,
        PlanDisplayName = "Unlimited",
        CanExport = true,
        ShopPhoneOnReceipts = true
    };
}

/// <summary>Owner: attach a license key to an existing shop (same checks as registration, email = the Owner's).</summary>
public sealed class AttachLicenseRequest
{
    [Required, MaxLength(200)]
    public string LicenseKey { get; set; } = string.Empty;
}
