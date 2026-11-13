namespace NeedleBase.Domain.Entities;

/// <summary>Workspace / shop. Tenant root — other entities reference <see cref="Id"/> as BusinessId.</summary>
public class Business : Entity
{
    public string Name { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string CurrencyCode { get; private set; } = "GHS";
    public string MeasurementUnit { get; private set; } = "cm";
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // ---- Platform license (see docs/platform-licensing.md). Null key = not attached yet (never locked). ----

    /// <summary>License key encrypted with ASP.NET Data Protection (never stored or logged in plain text).</summary>
    public string? LicenseKeyProtected { get; private set; }

    /// <summary>Last 4 characters of the key, for "key ending …" on the settings page.</summary>
    public string? LicenseKeyHint { get; private set; }

    public DateTimeOffset? LicenseAttachedAtUtc { get; private set; }

    /// <summary>Platform's last judged reason: "ok", "expired", "suspended", "revoked", "invalid_key", …</summary>
    public string? LicenseReason { get; private set; }

    public bool LicenseIsValid { get; private set; }
    public string? LicenseId { get; private set; }
    public string? LicensePlan { get; private set; }
    public bool LicenseIsTrial { get; private set; }
    public bool LicenseIsComplimentary { get; private set; }
    public DateTimeOffset? LicenseExpiresAtUtc { get; private set; }
    public DateTimeOffset? LicenseRefreshAfterUtc { get; private set; }

    /// <summary>Offline grace from the last valid signed token (Platform's <c>grace_until</c>).</summary>
    public DateTimeOffset? LicenseGraceUntilUtc { get; private set; }

    /// <summary>Last verified signed token (ES256) from Platform.</summary>
    public string? LicenseToken { get; private set; }

    /// <summary>When Platform last gave a definite answer (valid or not). Network failures don't move it.</summary>
    public DateTimeOffset? LicenseCheckedAtUtc { get; private set; }

    /// <summary>When the license first came back not valid (cleared when it is valid again).</summary>
    public DateTimeOffset? LicenseInvalidSinceUtc { get; private set; }

    /// <summary>When the background re-check should next call Platform.</summary>
    public DateTimeOffset? LicenseNextCheckUtc { get; private set; }

    /// <summary>Consecutive "couldn't reach Platform" re-checks (drives the back-off; never a lock).</summary>
    public int LicenseFailedChecks { get; private set; }

    /// <summary>When the Owner closed the shop (account deletion). Null while the shop is open.</summary>
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public bool IsClosed => ClosedAtUtc is not null;

    private Business()
    {
    }

    public static Business Create(string id, string name, string? phone, DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Business
        {
            Id = id,
            Name = name.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            CurrencyCode = "GHS",
            MeasurementUnit = "cm",
            CreatedAtUtc = createdAtUtc
        };
    }

    /// <summary>Attach (or replace) the shop's license after Platform accepted it.</summary>
    public void AttachLicense(string protectedKey, string keyHint, LicenseCheck check, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedKey);
        LicenseKeyProtected = protectedKey;
        LicenseKeyHint = keyHint;
        LicenseAttachedAtUtc = now;
        LicenseInvalidSinceUtc = null;
        LicenseGraceUntilUtc = null;
        RecordLicenseCheck(check, now);
    }

    /// <summary>Platform gave a definite answer. A "not valid" answer starts (or keeps) the invalid clock.</summary>
    public void RecordLicenseCheck(LicenseCheck check, DateTimeOffset now)
    {
        LicenseCheckedAtUtc = now;
        LicenseFailedChecks = 0;
        LicenseReason = check.Reason;
        LicenseIsValid = check.IsValid;
        if (check.IsValid)
        {
            LicenseInvalidSinceUtc = null;
            LicenseId = check.LicenseId ?? LicenseId;
            LicensePlan = check.Plan;
            LicenseIsTrial = check.IsTrial;
            LicenseIsComplimentary = check.IsComplimentary;
            LicenseExpiresAtUtc = check.ExpiresAtUtc;
            LicenseRefreshAfterUtc = check.RefreshAfterUtc;
            LicenseGraceUntilUtc = check.GraceUntilUtc;
            LicenseToken = check.Token;
            var next = check.RefreshAfterUtc ?? now.AddHours(12);
            // Platform's refresh hint, bounded so a bad value neither hammers Platform nor goes silent for weeks.
            LicenseNextCheckUtc = next < now.AddMinutes(30) ? now.AddMinutes(30) : next > now.AddDays(1) ? now.AddDays(1) : next;
        }
        else
        {
            LicenseInvalidSinceUtc ??= now;
            if (check.ExpiresAtUtc is not null)
            {
                LicenseExpiresAtUtc = check.ExpiresAtUtc;
            }

            // Keep checking so a renewal unlocks the shop without anyone pressing anything.
            LicenseNextCheckUtc = now.AddHours(6);
        }
    }

    /// <summary>Platform couldn't be reached / was busy: nothing about the license changes; try again later.</summary>
    public void RecordLicenseCheckUnavailable(DateTimeOffset now, TimeSpan? retryAfter)
    {
        LicenseFailedChecks = Math.Min(LicenseFailedChecks + 1, 20);
        var backoff = TimeSpan.FromMinutes(Math.Min(360, 15 * Math.Pow(2, Math.Min(LicenseFailedChecks - 1, 5))));
        if (retryAfter is { } wait && wait > backoff && wait < TimeSpan.FromDays(1))
        {
            backoff = wait;
        }

        LicenseNextCheckUtc = now + backoff;
    }

    /// <summary>
    /// Owner has deleted the shop account: block further use, drop the licence secrets, and anonymise the shop name.
    /// Hosted shop records are purged by the account-deletion service.
    /// </summary>
    public void Close(DateTimeOffset now)
    {
        if (ClosedAtUtc is not null)
        {
            return;
        }

        ClosedAtUtc = now;
        Name = "Closed shop";
        Phone = null;
        LicenseKeyProtected = null;
        LicenseKeyHint = null;
        LicenseToken = null;
        LicenseIsValid = false;
        LicenseReason = "closed";
        LicenseId = null;
        LicensePlan = null;
        LicenseIsTrial = false;
        LicenseIsComplimentary = false;
        LicenseExpiresAtUtc = null;
        LicenseRefreshAfterUtc = null;
        LicenseGraceUntilUtc = null;
        LicenseCheckedAtUtc = null;
        LicenseInvalidSinceUtc = null;
        LicenseNextCheckUtc = null;
        LicenseFailedChecks = 0;
    }
}

/// <summary>One definite answer from Platform (token claims when valid; the reason when not).</summary>
public sealed record LicenseCheck(
    bool IsValid,
    string Reason,
    string? LicenseId = null,
    string? Plan = null,
    bool IsTrial = false,
    bool IsComplimentary = false,
    DateTimeOffset? ExpiresAtUtc = null,
    DateTimeOffset? RefreshAfterUtc = null,
    DateTimeOffset? GraceUntilUtc = null,
    string? Token = null);
