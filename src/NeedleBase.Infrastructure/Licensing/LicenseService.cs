using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Licensing;

/// <summary>
/// The shop's Platform license: checks at registration/attach, encrypted storage, cached status (30 s per shop)
/// and the background re-check. See docs/platform-licensing.md.
/// </summary>
public sealed class LicenseService
{
    private const string Purpose = "NeedleBase.Business.LicenseKey.v1";
    private static readonly TimeSpan StatusTtl = TimeSpan.FromSeconds(30);

    /// <summary>How long a re-check holds a shop before another pass/instance may try it again.</summary>
    private static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(10);

    /// <summary>Longest key accepted (same as the request DTOs); longer is refused without calling Platform.</summary>
    public const int MaxKeyLength = 200;

    private readonly ApplicationDbContext _db;
    private readonly ILicensePlatform _platform;
    private readonly IDataProtector _protector;
    private readonly IOptionsMonitor<PlatformOptions> _options;
    private readonly IClock _clock;
    private readonly IMemoryCache _cache;
    private readonly IAuditLog _audit;
    private readonly ILogger<LicenseService> _logger;

    public LicenseService(
        ApplicationDbContext db,
        ILicensePlatform platform,
        IDataProtectionProvider dataProtection,
        IOptionsMonitor<PlatformOptions> options,
        IClock clock,
        IMemoryCache cache,
        IAuditLog audit,
        ILogger<LicenseService> logger)
    {
        _db = db;
        _platform = platform;
        _protector = dataProtection.CreateProtector(Purpose);
        _options = options;
        _clock = clock;
        _cache = cache;
        _audit = audit;
        _logger = logger;
    }

    public bool RequireLicenseKey => _options.CurrentValue.RequireLicenseKey;

    public static string Normalize(string? key) => (key ?? string.Empty).Trim();

    public string Protect(string key) => _protector.Protect(Normalize(key));

    /// <summary>Null when the key can't be decrypted (Data Protection keys lost/changed).</summary>
    public string? TryUnprotect(string? protectedKey)
    {
        if (string.IsNullOrEmpty(protectedKey))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedKey);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asks Platform about a key for a shop (new or existing) with the Owner's email. Returns the verified license on
    /// success, else a <see cref="LicenseErrors"/> code (+ Retry-After when Platform was busy/unreachable).
    /// </summary>
    public async Task<(LicenseCheck? Check, string? ErrorCode, TimeSpan? RetryAfter)> CheckKeyAsync(
        string licenseKey, string shopId, string? shopName, string ownerEmail, CancellationToken cancellationToken)
    {
        var key = Normalize(licenseKey);
        if (key.Length == 0)
        {
            return (null, LicenseErrors.KeyRequired, null);
        }

        if (key.Length > MaxKeyLength || key.Any(char.IsControl))
        {
            return (null, LicenseErrors.KeyInvalid, null);
        }

        if (!_platform.IsConfigured)
        {
            _logger.LogError("A license key was entered but Platform:BaseUrl / Platform:IntegrationKey are not set; refused.");
            return (null, LicenseErrors.NotConfigured, null);
        }

        var outcome = await _platform.ValidateAsync(
            new LicenseValidateCall(key, shopId, shopName, ownerEmail.Trim()), cancellationToken).ConfigureAwait(false);
        var code = LicenseErrors.Decide(outcome);
        if (code is not null)
        {
            _logger.LogInformation("License check for shop {ShopId} refused: {Code} ({Reason})", shopId, code,
                outcome.Check?.Reason ?? outcome.UnavailableReason);
            return (null, code, outcome.RetryAfter);
        }

        return (outcome.Check, null, null);
    }

    /// <summary>Owner: attach (or replace) the key on an existing shop. Same checks as registration.</summary>
    public async Task<(SubscriptionStatusDto? Status, string? ErrorCode, TimeSpan? RetryAfter)> AttachAsync(
        string businessId, string userId, string licenseKey, CancellationToken cancellationToken)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken).ConfigureAwait(false);
        var email = await _db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (business is null || string.IsNullOrWhiteSpace(email))
        {
            return (null, "BUSINESS_MISSING", null);
        }

        var (check, code, retry) = await CheckKeyAsync(licenseKey, business.Id, business.Name, email, cancellationToken).ConfigureAwait(false);
        if (check is null)
        {
            return (null, code, retry);
        }

        var now = _clock.UtcNow;
        business.AttachLicense(Protect(licenseKey), SubscriptionRules.KeyHint(licenseKey), check, now);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidate(business.Id);
        await _audit.WriteAsync(new AuditEntry(AuditActions.LicenseAttached,
                $"License key {SubscriptionRules.KeyHint(licenseKey)} added", business.Id, userId, BusinessRole.Owner, "business", business.Id),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return (SubscriptionRules.Compute(business, now, _options.CurrentValue.LockGraceDays), null, null);
    }

    /// <summary>Status for everyone in the shop (cached 30 s; attach and re-check clear it).</summary>
    public async Task<SubscriptionStatusDto> GetStatusAsync(string businessId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey(businessId), out SubscriptionStatusDto? cached) && cached is not null)
        {
            return cached;
        }

        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken).ConfigureAwait(false);
        var status = business is null
            ? SubscriptionStatusDto.Unlocked
            : SubscriptionRules.Compute(business, _clock.UtcNow, _options.CurrentValue.LockGraceDays,
                keyReadable: business.LicenseKeyProtected is null || TryUnprotect(business.LicenseKeyProtected) is not null);
        _cache.Set(CacheKey(businessId), status, StatusTtl);
        return status;
    }

    public void Invalidate(string businessId) => _cache.Remove(CacheKey(businessId));

    /// <summary>
    /// Background re-check of shops whose <c>LicenseNextCheckUtc</c> is due (Platform's refresh_after). Platform
    /// unreachable / 429 / 503 only reschedules; it never changes the license. Returns how many shops were looked at.
    /// </summary>
    public async Task<int> RecheckDueAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (!_platform.IsConfigured)
        {
            return 0;
        }

        var now = _clock.UtcNow;
        var due = await _db.Businesses
            .AsNoTracking()
            .Where(b => b.LicenseKeyProtected != null && (b.LicenseNextCheckUtc == null || b.LicenseNextCheckUtc <= now))
            .OrderBy(b => b.LicenseNextCheckUtc)
            .Select(b => new { b.Id, b.LicenseNextCheckUtc })
            .Take(batchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var claimUntil = now + ClaimFor;
        foreach (var item in due)
        {
            // Claim the shop first (conditional update), so a second Api instance or an overlapping pass skips it
            // instead of asking Platform twice. A crash mid-check just leaves it due again after ClaimFor.
            var claimed = await _db.Businesses
                .Where(b => b.Id == item.Id && b.LicenseNextCheckUtc == item.LicenseNextCheckUtc)
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.LicenseNextCheckUtc, claimUntil), cancellationToken).ConfigureAwait(false);
            if (claimed == 0)
            {
                continue;
            }

            var stop = await RecheckOneAsync(item.Id, cancellationToken).ConfigureAwait(false);
            Invalidate(item.Id);
            if (stop)
            {
                break; // Platform is busy/down: leave the rest for the next pass (they keep their due time).
            }
        }

        return due.Count;
    }

    /// <summary>Checks one claimed shop and saves straight away. True when Platform looks down/busy.</summary>
    private async Task<bool> RecheckOneAsync(string businessId, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken).ConfigureAwait(false);
        if (business?.LicenseKeyProtected is not { } protectedKey)
        {
            return false;
        }

        var key = TryUnprotect(protectedKey);
        var stop = false;
        if (key is null)
        {
            _logger.LogWarning("Shop {ShopId}: stored license key can't be decrypted (Data Protection keys changed?). Owner must re-enter it.", business.Id);
            business.RecordLicenseCheckUnavailable(_clock.UtcNow, TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1));
        }
        else
        {
            var outcome = await _platform.ValidateAsync(new LicenseValidateCall(key, business.Id, business.Name, null), cancellationToken)
                .ConfigureAwait(false);

            // The Owner may have attached a different key while we waited for Platform: that answer wins.
            var current = await _db.Businesses.AsNoTracking().Where(b => b.Id == business.Id).Select(b => b.LicenseKeyProtected)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(current, protectedKey, StringComparison.Ordinal))
            {
                return false;
            }

            if (outcome.Unavailable || outcome.Check is null)
            {
                business.RecordLicenseCheckUnavailable(_clock.UtcNow, outcome.RetryAfter);
                stop = outcome.UnavailableReason is "rate_limited" or "service_unavailable" or "not_configured" or "network" or "timeout"
                    or "jwks_unavailable";
            }
            else
            {
                if (business.LicenseIsValid && !outcome.Check.IsValid)
                {
                    _logger.LogWarning("Shop {ShopId}: license is no longer valid ({Reason})", business.Id, outcome.Check.Reason);
                }

                business.RecordLicenseCheck(outcome.Check, _clock.UtcNow);
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return stop;
    }

    private static string CacheKey(string businessId) => "subscription:" + businessId;

    /// <summary>SHA-256 of key + email: lets a retried registration reuse the same shop id (see <see cref="PendingShopIds"/>).</summary>
    public static string AttemptFingerprint(string licenseKey, string email) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(licenseKey) + "\n" + email.Trim().ToLowerInvariant())));
}
