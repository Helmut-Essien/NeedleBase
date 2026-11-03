using System.Globalization;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Application.Licensing;

/// <summary>
/// Turns the cached license on a <see cref="Business"/> into what people see. Soft lock only:
/// read-only starts when Platform's last definite answer was "not valid" AND both the signed token's grace and
/// <c>lockGraceDays</c> after the first "not valid" answer have passed. Network errors, 429 and 503 never move
/// any of these dates, so they can never lock a shop. No key yet (older shops) = never locked.
/// </summary>
public static class SubscriptionRules
{
    public const int ExpiryWarningDays = 14;

    public static SubscriptionStatusDto Compute(Business business, DateTimeOffset now, int lockGraceDays, bool keyReadable = true)
    {
        if (string.IsNullOrEmpty(business.LicenseKeyProtected))
        {
            return new SubscriptionStatusDto
            {
                State = SubscriptionStates.NotAttached,
                OwnerOnly = true,
                Message = "Add your NeedleBase license key on the License page of the NeedleBase website to keep your shop covered."
            };
        }

        if (!keyReadable)
        {
            return new SubscriptionStatusDto
            {
                State = SubscriptionStates.NeedsReattach,
                OwnerOnly = true,
                KeyHint = business.LicenseKeyHint,
                LastCheckedUtc = business.LicenseCheckedAtUtc,
                Message = "Please enter your NeedleBase license key again on the License page of the NeedleBase website."
            };
        }

        if (business.LicenseIsValid || business.LicenseInvalidSinceUtc is null)
        {
            var state = business.LicenseIsComplimentary ? SubscriptionStates.Complimentary
                : business.LicenseIsTrial ? SubscriptionStates.Trial
                : SubscriptionStates.Active;
            string? message = null;
            if (!business.LicenseIsComplimentary && business.LicenseExpiresAtUtc is { } ends &&
                ends > now && ends - now <= TimeSpan.FromDays(ExpiryWarningDays))
            {
                message = business.LicenseIsTrial
                    ? $"Your NeedleBase trial ends on {Day(ends)}. Renew to keep adding work."
                    : $"Your NeedleBase license ends on {Day(ends)}. Renew to keep adding work.";
            }

            return Base(business, state, message);
        }

        var readOnlyFrom = ReadOnlyFrom(business, lockGraceDays)!.Value;
        var phrase = LicenseErrors.Phrase(business.LicenseReason);
        if (now >= readOnlyFrom)
        {
            return Base(business, SubscriptionStates.ReadOnly,
                $"Your NeedleBase license {phrase}. New changes are paused; you can still view your records and changes made earlier will still sync. Renew to carry on.")
                with { IsReadOnly = true, ReadOnlyFromUtc = readOnlyFrom };
        }

        return Base(business, SubscriptionStates.Problem,
                $"Your NeedleBase license {phrase}. Renew before {Day(readOnlyFrom)} to keep adding new work.")
            with { ReadOnlyFromUtc = readOnlyFrom };
    }

    /// <summary>max(token grace_until, first "not valid" + lockGraceDays); null while valid.</summary>
    public static DateTimeOffset? ReadOnlyFrom(Business business, int lockGraceDays)
    {
        if (business.LicenseIsValid || business.LicenseInvalidSinceUtc is not { } since)
        {
            return null;
        }

        var ours = since.AddDays(Math.Max(0, lockGraceDays));
        return business.LicenseGraceUntilUtc is { } grace && grace > ours ? grace : ours;
    }

    private static SubscriptionStatusDto Base(Business b, string state, string? message) => new()
    {
        State = state,
        Message = message,
        Plan = b.LicensePlan,
        IsTrial = b.LicenseIsTrial,
        IsComplimentary = b.LicenseIsComplimentary,
        ExpiresAtUtc = b.LicenseExpiresAtUtc,
        KeyHint = b.LicenseKeyHint,
        LastCheckedUtc = b.LicenseCheckedAtUtc
    };

    // Shops are in Ghana (GMT, no DST): the UTC date is the local date.
    private static string Day(DateTimeOffset when) =>
        when.UtcDateTime.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB"));

    /// <summary>"…ABCD" hint for a key (never more than the last 4 characters).</summary>
    public static string KeyHint(string key)
    {
        var trimmed = key.Trim();
        return trimmed.Length <= 4 ? "…" : "…" + trimmed[^4..];
    }
}
