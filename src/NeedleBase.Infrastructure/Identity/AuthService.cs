using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Staff;
using NeedleBase.Application.Email;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace NeedleBase.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;
    private readonly LoginThrottle _throttle;
    private readonly ResetRequestIpCap _resetIpCap;
    private readonly IAuditLog _audit;
    private readonly IEmailSender _email;
    private readonly IOptionsMonitor<Email.EmailOptions> _emailOptions;

    /// <summary>Failed sign-ins for one email+IP: one audit row per window, with a count of the rest.</summary>
    public static readonly TimeSpan FailedLoginAuditWindow = TimeSpan.FromMinutes(15);

    public AuthService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        ITokenService tokenService,
        IClock clock,
        LoginThrottle throttle,
        ResetRequestIpCap resetIpCap,
        IAuditLog audit,
        IEmailSender email,
        IOptionsMonitor<Email.EmailOptions> emailOptions)
    {
        _email = email;
        _emailOptions = emailOptions;
        _throttle = throttle;
        _resetIpCap = resetIpCap;
        _audit = audit;
        _userManager = userManager;
        _db = db;
        _tokenService = tokenService;
        _clock = clock;
    }

    public async Task<AuthResult> RegisterBusinessAsync(
        RegisterBusinessRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim();
        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            return AuthResult.Fail("Unable to register with the provided details.", "REGISTER_FAILED");
        }

        var now = _clock.UtcNow;
        var businessId = IdGenerator.NewId();
        var business = Business.Create(businessId, request.BusinessName, request.BusinessPhone, now);

        var user = new ApplicationUser
        {
            Id = IdGenerator.NewId(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = request.FirstName.Trim(),
            Surname = request.Surname.Trim(),
            IsActive = true,
            CreatedAtUtc = now
        };

        var strategy = _db.Database.CreateExecutionStrategy();
        AuthResult? identityFailure = null;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var createResult = await _userManager.CreateAsync(user, request.Password);
                if (!createResult.Succeeded)
                {
                    identityFailure = AuthResult.Fail(
                        "Unable to register with the provided details.",
                        "REGISTER_FAILED");
                    await tx.RollbackAsync(cancellationToken);
                    return;
                }

                var membership = StaffMembership.Create(
                    IdGenerator.NewId(),
                    businessId,
                    user.Id,
                    BusinessRole.Owner,
                    now);

                _db.CurrentBusinessId = null;
                _db.Businesses.Add(business);
                _db.StaffMemberships.Add(membership);
                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });

        if (identityFailure is not null)
        {
            return identityFailure;
        }

        await _audit.WriteAsync(new AuditEntry(AuditActions.Registered, "Registered the business owner account", businessId, user.Id, BusinessRole.Owner),
            cancellationToken: cancellationToken);
        return await IssueTokensAsync(user, business, BusinessRole.Owner, ipAddress, cancellationToken);
    }

    public async Task<AuthResult> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim();
        var failKey = $"auth.fail:{email.ToLowerInvariant()}:{ipAddress}";
        if (_throttle.RetryAfter(email, ipAddress) is { } wait)
        {
            var throttledUser = await _userManager.FindByEmailAsync(email);
            await _audit.WriteAsync(
                new AuditEntry(AuditActions.LoginThrottled, "Sign-in slowed down after repeated failures",
                    throttledUser is null ? null : await BusinessOfAsync(throttledUser.Id, cancellationToken), throttledUser?.Id),
                "auth.throttled:" + failKey, FailedLoginAuditWindow, cancellationToken); // own count, not folded into failures
            return AuthResult.Throttled(wait);
        }

        var user = await _userManager.FindByEmailAsync(email);
        // Password first: account state (deactivated) is only revealed to someone who knows the password.
        // No account-wide lockout (Identity lockout is off; any old LockoutEnd is ignored): see LoginThrottle.
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            _throttle.RecordFailure(email, ipAddress);
            // The client answer is the same either way (no enumeration); the owner's log says which it was.
            await _audit.WriteAsync(user is null
                    ? new AuditEntry(AuditActions.LoginFailed, $"Sign-in failed for an unknown account ({AuditMask.Email(email)})")
                    : new AuditEntry(AuditActions.LoginFailed, "Sign-in failed: wrong password", await BusinessOfAsync(user.Id, cancellationToken), user.Id),
                failKey, FailedLoginAuditWindow, cancellationToken);
            return AuthResult.Fail("Invalid email or password.", "INVALID_CREDENTIALS");
        }

        _throttle.RecordSuccess(email, ipAddress);
        if (!user.IsActive)
        {
            await _audit.WriteAsync(new AuditEntry(AuditActions.LoginBlocked, "Sign-in refused: account deactivated",
                await BusinessOfAsync(user.Id, cancellationToken, includeInactive: true), user.Id), cancellationToken: cancellationToken);
            return AuthResult.Fail("Account deactivated. Contact your business owner.", "ACCOUNT_DEACTIVATED");
        }

        _db.CurrentBusinessId = null;
        var membership = await _db.StaffMemberships
            .IgnoreQueryFilters() // cross-business by design: scoped to this user's id
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.IsActive)
            .OrderBy(m => m.JoinedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            var turnedOff = await _db.StaffMemberships.IgnoreQueryFilters().AnyAsync(m => m.UserId == user.Id && !m.IsActive, cancellationToken);
            await _audit.WriteAsync(new AuditEntry(AuditActions.LoginBlocked,
                turnedOff ? "Sign-in refused: staff access turned off" : "Sign-in refused: no active business membership",
                await BusinessOfAsync(user.Id, cancellationToken, includeInactive: true), user.Id), cancellationToken: cancellationToken);
            // Staff whose access the owner turned off hear that plainly (same wipe on the phone as NO_MEMBERSHIP).
            return turnedOff
                ? AuthResult.Fail("Account deactivated. Contact your business owner.", "ACCOUNT_DEACTIVATED")
                : AuthResult.Fail("No business membership found for this account.", "NO_MEMBERSHIP");
        }

        var business = await _db.Businesses.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == membership.BusinessId, cancellationToken);
        if (business is null)
        {
            return AuthResult.Fail("Business not found.", "BUSINESS_MISSING");
        }

        await _audit.WriteAsync(new AuditEntry(AuditActions.LoginSucceeded, "Signed in", business.Id, user.Id, membership.Role),
            cancellationToken: cancellationToken);
        return await IssueTokensAsync(user, business, membership.Role, ipAddress, cancellationToken);
    }

    /// <summary>The business a user's security events belong to (earliest membership), or null.</summary>
    private async Task<string?> BusinessOfAsync(string userId, CancellationToken cancellationToken, bool includeInactive = false)
    {
        _db.CurrentBusinessId = null;
        return await _db.StaffMemberships.IgnoreQueryFilters().AsNoTracking() // cross-business by design: this user only
            .Where(m => m.UserId == userId && (includeInactive || m.IsActive))
            .OrderByDescending(m => m.IsActive).ThenBy(m => m.JoinedAtUtc)
            .Select(m => m.BusinessId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AuthResult> RefreshAsync(
        string refreshToken,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthResult.Fail("Refresh token is required.", "REFRESH_REQUIRED");
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                _db.CurrentBusinessId = null;
                var tokenHash = RefreshTokenHasher.Hash(refreshToken);
                var existing = await _db.RefreshTokens
                    .Include(t => t.ApplicationUser)
                    .FirstOrDefaultAsync(t => t.Token == tokenHash, cancellationToken);

                if (existing is null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return AuthResult.Fail("Invalid or expired refresh token.", "REFRESH_INVALID");
                }

                if (existing.RevokedUtc is not null)
                {
                    // A phone that lost the response to its last refresh retries with the old token. Inside a short
                    // window, and only once, and only while the successor is still unused, rotate the successor
                    // instead of treating it as theft. Anything else is reuse → revoke the whole family.
                    var successor = await GraceSuccessorAsync(existing, cancellationToken);
                    if (successor is null)
                    {
                        await RevokeAllRefreshTokensForUserAsync(existing.ApplicationUserId, ipAddress, cancellationToken);
                        await tx.CommitAsync(cancellationToken);
                        await _audit.WriteAsync(new AuditEntry(AuditActions.TokenReuse,
                                "An old sign-in token was used again; all of this person's sessions were signed out",
                                await BusinessOfAsync(existing.ApplicationUserId, cancellationToken, includeInactive: true), existing.ApplicationUserId),
                            cancellationToken: cancellationToken);
                        return AuthResult.Fail("Invalid or expired refresh token.", "REFRESH_REUSE");
                    }

                    existing.GraceUsedUtc = DateTime.UtcNow;
                    existing = successor;
                }

                if (existing.IsExpired || existing.ApplicationUser is null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return AuthResult.Fail("Invalid or expired refresh token.", "REFRESH_INVALID");
                }

                var user = existing.ApplicationUser;
                if (!user.IsActive)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return AuthResult.Fail("Account deactivated.", "ACCOUNT_DEACTIVATED");
                }

                var membership = await _db.StaffMemberships
                    .IgnoreQueryFilters() // cross-business by design: scoped to this user's id
                    .AsNoTracking()
                    .Where(m => m.UserId == user.Id && m.IsActive)
                    .OrderBy(m => m.JoinedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (membership is null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return AuthResult.Fail("No business membership found.", "NO_MEMBERSHIP");
                }

                var business = await _db.Businesses.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == membership.BusinessId, cancellationToken);
                if (business is null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return AuthResult.Fail("Business not found.", "BUSINESS_MISSING");
                }

                var replacement = _tokenService.CreateRefreshToken(ipAddress);
                existing.RevokedUtc = DateTime.UtcNow;
                existing.RevokedByIp = ipAddress;
                existing.ReplacedByToken = RefreshTokenHasher.Hash(replacement.Token);

                _db.RefreshTokens.Add(new RefreshToken
                {
                    Id = IdGenerator.NewId(),
                    Token = RefreshTokenHasher.Hash(replacement.Token),
                    ExpiresUtc = replacement.ExpiresUtc,
                    CreatedUtc = replacement.CreatedUtc,
                    CreatedByIp = replacement.CreatedByIp,
                    ApplicationUserId = user.Id
                });

                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                var access = _tokenService.CreateAccessToken(
                    user.Id,
                    user.Email ?? string.Empty,
                    user.UserName ?? user.Email ?? user.Id,
                    user.SecurityStamp ?? string.Empty,
                    user.IsActive,
                    business.Id,
                    membership.Role);

                var tokens = new TokenResponse(access, replacement.Token);
                var dto = new AuthUserDto(
                    user.Id,
                    user.Email ?? string.Empty,
                    user.FirstName,
                    user.Surname,
                    business.Id,
                    business.Name,
                    membership.Role);

                return AuthResult.Ok(tokens, dto);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task LogoutAsync(
        string? refreshToken,
        string? userId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        _db.CurrentBusinessId = null;

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var tokenHash = RefreshTokenHasher.Hash(refreshToken);
            var token = await _db.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == tokenHash, cancellationToken);
            if (token is not null && token.RevokedUtc is null)
            {
                token.RevokedUtc = DateTime.UtcNow;
                token.RevokedByIp = ipAddress;
                await _db.SaveChangesAsync(cancellationToken);
                await LogLogoutAsync(token.ApplicationUserId, cancellationToken);
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            await RevokeAllRefreshTokensForUserAsync(userId, ipAddress, cancellationToken);
            await LogLogoutAsync(userId, cancellationToken);
        }
    }

    private async Task LogLogoutAsync(string userId, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new AuditEntry(AuditActions.Logout, "Signed out",
            await BusinessOfAsync(userId, cancellationToken, includeInactive: true), userId), cancellationToken: cancellationToken);

    /// <summary>How long after a rotation the previous refresh token may be presented once more (lost response).</summary>
    public static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The successor to rotate when a rotated token comes back inside <see cref="RefreshReuseGrace"/>: only for a
    /// token replaced by rotation (logout leaves no successor), never used for grace before, and whose successor
    /// was never used (so a thief replaying it after the device has moved on gets nothing). Plain-text tokens are
    /// not stored, so the device receives a fresh successor rather than the same one; the unused one is revoked.
    /// </summary>
    private async Task<RefreshToken?> GraceSuccessorAsync(RefreshToken rotated, CancellationToken cancellationToken)
    {
        if (rotated.ReplacedByToken is null || rotated.GraceUsedUtc is not null || rotated.RevokedUtc is not { } revokedAt ||
            DateTime.UtcNow - revokedAt > RefreshReuseGrace)
        {
            return null;
        }

        var successor = await _db.RefreshTokens
            .Include(t => t.ApplicationUser)
            .FirstOrDefaultAsync(t => t.Token == rotated.ReplacedByToken, cancellationToken);
        return successor is { IsActive: true } && successor.ApplicationUserId == rotated.ApplicationUserId ? successor : null;
    }

    private async Task RevokeAllRefreshTokensForUserAsync(
        string userId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await _db.RefreshTokens
            .Where(t => t.ApplicationUserId == userId && t.RevokedUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedUtc, now)
                    .SetProperty(t => t.RevokedByIp, ipAddress),
                cancellationToken);
    }

    private async Task<AuthResult> IssueTokensAsync(
        ApplicationUser user,
        Business business,
        string role,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var refreshInfo = _tokenService.CreateRefreshToken(ipAddress);
        var tracked = await _userManager.FindByIdAsync(user.Id)
            ?? throw new InvalidOperationException("User missing after create/login.");

        tracked.RefreshTokens.Add(new RefreshToken
        {
            Id = IdGenerator.NewId(),
            Token = RefreshTokenHasher.Hash(refreshInfo.Token),
            ExpiresUtc = refreshInfo.ExpiresUtc,
            CreatedUtc = refreshInfo.CreatedUtc,
            CreatedByIp = refreshInfo.CreatedByIp,
            ApplicationUserId = tracked.Id
        });

        await _userManager.UpdateAsync(tracked);

        var access = _tokenService.CreateAccessToken(
            tracked.Id,
            tracked.Email ?? string.Empty,
            tracked.UserName ?? tracked.Email ?? tracked.Id,
            tracked.SecurityStamp ?? string.Empty,
            tracked.IsActive,
            business.Id,
            role);

        var tokens = new TokenResponse(access, refreshInfo.Token);
        var dto = new AuthUserDto(
            tracked.Id,
            tracked.Email ?? string.Empty,
            tracked.FirstName,
            tracked.Surname,
            business.Id,
            business.Name,
            role);

        return AuthResult.Ok(tokens, dto);
    }

    // ---- Slice 7: invites + password change ----

    /// <summary>Throttle key for invite-code guesses: per IP (the code is the secret, not an email).</summary>
    private static string InviteThrottleKey => "invite-code";

    public async Task<(InvitePreviewDto? Preview, string? ErrorCode, TimeSpan? RetryAfter)> PreviewInviteAsync(
        string code, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (_throttle.RetryAfter(InviteThrottleKey, ipAddress) is { } wait)
        {
            return (null, AuthResult.LoginThrottledCode, wait);
        }

        var (invite, error) = await FindUsableInviteAsync(code, ipAddress, cancellationToken);
        if (invite is null)
        {
            return (null, error, null);
        }

        var businessName = await _db.Businesses.AsNoTracking().Where(b => b.Id == invite.BusinessId)
            .Select(b => b.Name).FirstOrDefaultAsync(cancellationToken);
        return businessName is null
            ? (null, StaffErrorMessages.InviteNotFound, null)
            : (new InvitePreviewDto(businessName, invite.Role, invite.Email, invite.Name, invite.ExpiresAtUtc), null, null);
    }

    /// <summary>Invite by code, or the stable error code. Unknown codes count towards the per-IP throttle.</summary>
    private async Task<(Invitation? Invite, string? Error)> FindUsableInviteAsync(string? code, string? ipAddress, CancellationToken cancellationToken)
    {
        var hash = InviteCodes.Hash(code);
        _db.CurrentBusinessId = null;
        var invite = hash is null
            ? null
            : await _db.Invitations.IgnoreQueryFilters() // by secret code, before any business is known
                .AsNoTracking().FirstOrDefaultAsync(i => i.CodeHash == hash, cancellationToken);
        if (invite is null)
        {
            _throttle.RecordFailure(InviteThrottleKey, ipAddress);
            return (null, StaffErrorMessages.InviteNotFound);
        }

        return invite.StatusAt(_clock.UtcNow) switch
        {
            InvitationStatus.Pending => (invite, null),
            InvitationStatus.Accepted => (null, StaffErrorMessages.InviteUsed),
            InvitationStatus.Revoked => (null, StaffErrorMessages.InviteRevoked),
            _ => (null, StaffErrorMessages.InviteExpired)
        };
    }

    public async Task<AuthResult> AcceptInviteAsync(AcceptInviteRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (_throttle.RetryAfter(InviteThrottleKey, ipAddress) is { } wait)
        {
            return AuthResult.Throttled(wait);
        }

        var (found, error) = await FindUsableInviteAsync(request.Code, ipAddress, cancellationToken);
        if (found is null)
        {
            return AuthResult.Fail(StaffErrorMessages.For(error), error);
        }

        if (await _userManager.FindByEmailAsync(found.Email) is not null)
        {
            return AuthResult.Fail(StaffErrorMessages.For(StaffErrorMessages.InviteEmailInUse), StaffErrorMessages.InviteEmailInUse);
        }

        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == found.BusinessId, cancellationToken);
        if (business is null)
        {
            return AuthResult.Fail(StaffErrorMessages.For(StaffErrorMessages.InviteNotFound), StaffErrorMessages.InviteNotFound);
        }

        var now = _clock.UtcNow;
        var user = new ApplicationUser
        {
            Id = IdGenerator.NewId(),
            UserName = found.Email,
            Email = found.Email,
            // The invite code reached them, but nothing proved they own the address; there is no email flow yet.
            EmailConfirmed = false,
            FirstName = request.FirstName.Trim(),
            Surname = request.Surname.Trim(),
            IsActive = true,
            CreatedAtUtc = now
        };

        AuthResult? failure = null;
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            failure = null;
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Row lock: two taps (or two phones) with the same code must create one account, not two.
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"Invitations\" WHERE \"Id\" = {found.Id} FOR UPDATE", cancellationToken);
                _db.CurrentBusinessId = null;
                var invite = await _db.Invitations.IgnoreQueryFilters().FirstAsync(i => i.Id == found.Id, cancellationToken);
                if (!invite.IsPendingAt(now))
                {
                    failure = AuthResult.Fail(StaffErrorMessages.For(StaffErrorMessages.InviteUsed), StaffErrorMessages.InviteUsed);
                    await tx.RollbackAsync(cancellationToken);
                    return;
                }

                var created = await _userManager.CreateAsync(user, request.Password);
                if (!created.Succeeded)
                {
                    var inUse = created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName");
                    var code = inUse ? StaffErrorMessages.InviteEmailInUse : StaffErrorMessages.PasswordWeak;
                    failure = AuthResult.Fail(StaffErrorMessages.For(code), code);
                    await tx.RollbackAsync(cancellationToken);
                    return;
                }

                invite.Accept(user.Id, now);
                _db.StaffMemberships.Add(StaffMembership.Create(IdGenerator.NewId(), invite.BusinessId, user.Id, invite.Role, now));
                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });

        if (failure is not null)
        {
            return failure;
        }

        _throttle.RecordSuccess(InviteThrottleKey, ipAddress);
        return await IssueTokensAsync(user, business, found.Role, ipAddress, cancellationToken);
    }

    public async Task<AuthResult> ChangePasswordAsync(
        string userId, ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            return AuthResult.Fail("Account deactivated.", "ACCOUNT_DEACTIVATED");
        }

        var email = user.Email ?? user.Id;
        if (_throttle.RetryAfter(email, ipAddress) is { } wait)
        {
            return AuthResult.Throttled(wait);
        }

        if (!await _userManager.CheckPasswordAsync(user, request.CurrentPassword))
        {
            // Same counter as sign-in: a stolen session can't be used to guess the password faster.
            _throttle.RecordFailure(email, ipAddress);
            return AuthResult.Fail(StaffErrorMessages.For(StaffErrorMessages.PasswordIncorrect), StaffErrorMessages.PasswordIncorrect);
        }

        _throttle.RecordSuccess(email, ipAddress);
        _db.CurrentBusinessId = null;
        var membership = await _db.StaffMemberships.IgnoreQueryFilters().AsNoTracking() // this user's own membership
            .Where(m => m.UserId == user.Id && m.IsActive)
            .OrderBy(m => m.JoinedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var business = membership is null
            ? null
            : await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == membership.BusinessId, cancellationToken);
        if (membership is null || business is null)
        {
            return AuthResult.Fail("No business membership found.", "NO_MEMBERSHIP");
        }

        // ChangePasswordAsync rotates the security stamp: every access token (this one included) stops working.
        var changed = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            return AuthResult.Fail(StaffErrorMessages.For(StaffErrorMessages.PasswordWeak), StaffErrorMessages.PasswordWeak);
        }

        // Deleted, not revoked: another device presenting a revoked token would look like token theft (REFRESH_REUSE).
        await _db.RefreshTokens.Where(t => t.ApplicationUserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await _audit.WriteAsync(new AuditEntry(AuditActions.PasswordChanged, "Password changed; other devices were signed out",
            business.Id, user.Id, membership.Role), cancellationToken: cancellationToken);
        return await IssueTokensAsync(user, business, membership.Role, ipAddress, cancellationToken);
    }

    /// <summary>
    /// Throttle keys per (address, IP): many Ghana phones share one carrier IP (CGNAT), so an IP-wide key would lock
    /// out strangers. Per-account caps (3 codes an hour, 5 tries a code) still bound mail and guessing.
    /// </summary>
    private static string ResetRequestThrottleKey(string? email) => "password-reset-request:" + ThrottleEmail(email);

    private static string ResetThrottleKey(string? email) => "password-reset:" + ThrottleEmail(email);

    private static string ThrottleEmail(string? email) => (email ?? string.Empty).Trim().Normalize().ToUpperInvariant();

    public async Task<AuthResult> RequestPasswordResetAsync(ForgotPasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!_email.IsConfigured)
        {
            return AuthResult.Fail(StaffErrorMessages.For(StaffErrorMessages.ResetUnavailable), StaffErrorMessages.ResetUnavailable);
        }

        if (_throttle.RetryAfter(ResetRequestThrottleKey(request.Email), ipAddress) is { } wait)
        {
            return AuthResult.Throttled(wait);
        }

        if (_resetIpCap.TryTake(ipAddress) is { } ipWait)
        {
            return AuthResult.Throttled(ipWait);
        }

        // Every request counts (5 free per address+IP an hour, then slowed); the account cap below limits mail too.
        _throttle.RecordFailure(ResetRequestThrottleKey(request.Email), ipAddress);
        var user = await _userManager.FindByEmailAsync((request.Email ?? string.Empty).Trim());
        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.Email))
        {
            return AuthResult.Done(); // same reply as a real account
        }

        // Turned off everywhere: a new password couldn't sign them in, so no email (same neutral reply).
        // One query, also used for the audit row below.
        var membership = await _db.StaffMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.JoinedAtUtc)
            .Select(x => new { x.BusinessId, x.Role, x.IsActive })
            .FirstOrDefaultAsync(cancellationToken);
        if (membership is not { IsActive: true })
        {
            return AuthResult.Done(); // no audit row: it would be an unthrottled write per request
        }

        var now = _clock.UtcNow;
        var hourAgo = now.AddHours(-1);
        var recent = await _db.PasswordResetCodes.CountAsync(c => c.ApplicationUserId == user.Id && c.CreatedAtUtc > hourAgo, cancellationToken);
        if (recent >= PasswordResetCode.MaxPerHour)
        {
            return AuthResult.Done(); // per-account cap: silently ignored
        }

        // Only the newest code works; rows older than a day are dropped.
        var dayAgo = now.AddDays(-1);
        await _db.PasswordResetCodes.Where(c => c.ApplicationUserId == user.Id && c.CreatedAtUtc < dayAgo).ExecuteDeleteAsync(cancellationToken);
        await _db.PasswordResetCodes.Where(c => c.ApplicationUserId == user.Id && c.UsedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAtUtc, now), cancellationToken);

        var code = PasswordResetCode.NewCode();
        _db.PasswordResetCodes.Add(new PasswordResetCode
        {
            Id = IdGenerator.NewId(),
            ApplicationUserId = user.Id,
            CodeHash = PasswordResetCode.Hash(user.Id, PasswordResetCode.Normalize(code)!),
            CreatedAtUtc = now,
            ExpiresAtUtc = now + PasswordResetCode.Lifetime
        });
        await _db.SaveChangesAsync(cancellationToken);

        var queued = _email.TryQueue(EmailTemplates.PasswordReset(user.Email, user.FirstName, code, PasswordResetCode.Lifetime,
            _emailOptions.CurrentValue.AppBaseUrl));
        await _audit.WriteAsync(new AuditEntry(AuditActions.PasswordResetRequested,
                queued ? "Password reset code emailed" : "Password reset code not emailed (send queue full)", membership.BusinessId, user.Id, membership.Role),
            cancellationToken: cancellationToken);
        return AuthResult.Done();
    }

    public async Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!_email.IsConfigured)
        {
            return AuthResult.Fail(StaffErrorMessages.For(StaffErrorMessages.ResetUnavailable), StaffErrorMessages.ResetUnavailable);
        }

        if (_throttle.RetryAfter(ResetThrottleKey(request.Email), ipAddress) is { } wait)
        {
            return AuthResult.Throttled(wait);
        }

        var now = _clock.UtcNow;
        var normalized = PasswordResetCode.Normalize(request.Code);
        var user = normalized is null ? null : await _userManager.FindByEmailAsync((request.Email ?? string.Empty).Trim());
        var row = user is null || !user.IsActive
            ? null
            : await _db.PasswordResetCodes.AsNoTracking()
                .Where(c => c.ApplicationUserId == user.Id && c.UsedAtUtc == null && c.ExpiresAtUtc > now &&
                            c.FailedAttempts < PasswordResetCode.MaxAttempts)
                .OrderByDescending(c => c.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        if (row is null || user is null || !SameHash(row.CodeHash, PasswordResetCode.Hash(user.Id, normalized!)))
        {
            _throttle.RecordFailure(ResetThrottleKey(request.Email), ipAddress);
            if (row is not null)
            {
                await _db.PasswordResetCodes.Where(c => c.Id == row.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1), cancellationToken);
            }

            return AuthResult.Fail(StaffErrorMessages.For(StaffErrorMessages.ResetCodeInvalid), StaffErrorMessages.ResetCodeInvalid);
        }

        // Check the new password before using up the code, so a weak one can be retried with the same email.
        foreach (var validator in _userManager.PasswordValidators)
        {
            if (!(await validator.ValidateAsync(_userManager, user, request.NewPassword)).Succeeded)
            {
                return AuthResult.Fail(StaffErrorMessages.For(StaffErrorMessages.PasswordWeak), StaffErrorMessages.PasswordWeak);
            }
        }

        // Single use even with two taps at once: only one request can flip UsedAtUtc.
        var claimed = await _db.PasswordResetCodes.Where(c => c.Id == row.Id && c.UsedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAtUtc, now), cancellationToken);
        if (claimed == 0)
        {
            return AuthResult.Fail(StaffErrorMessages.For(StaffErrorMessages.ResetCodeInvalid), StaffErrorMessages.ResetCodeInvalid);
        }

        user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, request.NewPassword);
        user.EmailConfirmed = true; // the code reached this inbox
        var updated = await _userManager.UpdateSecurityStampAsync(user); // saves the hash; every access token stops working
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException("Couldn't save the new password.");
        }

        await _db.RefreshTokens.Where(t => t.ApplicationUserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        _throttle.RecordSuccess(ResetThrottleKey(request.Email), ipAddress);
        _throttle.RecordSuccess(user.Email ?? user.Id, ipAddress); // sign-in slow-down for this email + phone is cleared
        var (businessId, role) = await AuditMembershipAsync(user.Id, cancellationToken);
        await _audit.WriteAsync(new AuditEntry(AuditActions.PasswordReset, "Password reset with an emailed code; all devices were signed out",
            businessId, user.Id, role), cancellationToken: cancellationToken);
        return AuthResult.Done();
    }

    private static bool SameHash(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(a), System.Text.Encoding.ASCII.GetBytes(b));

    private async Task<(string? BusinessId, string? Role)> AuditMembershipAsync(string userId, CancellationToken cancellationToken)
    {
        var m = await _db.StaffMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.JoinedAtUtc)
            .Select(x => new { x.BusinessId, x.Role })
            .FirstOrDefaultAsync(cancellationToken);
        return (m?.BusinessId, m?.Role);
    }
}
