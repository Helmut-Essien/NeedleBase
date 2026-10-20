using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        ITokenService tokenService,
        IClock clock)
    {
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

        return await IssueTokensAsync(user, business, BusinessRole.Owner, ipAddress, cancellationToken);
    }

    public async Task<AuthResult> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return AuthResult.Fail("Invalid email or password.", "INVALID_CREDENTIALS");
        }

        if (!user.IsActive)
        {
            return AuthResult.Fail("Account deactivated. Contact your business owner.", "ACCOUNT_DEACTIVATED");
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return AuthResult.Fail("Too many failed attempts. Try again later.", "ACCOUNT_LOCKED");
        }

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            await _userManager.AccessFailedAsync(user);
            return AuthResult.Fail("Invalid email or password.", "INVALID_CREDENTIALS");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        _db.CurrentBusinessId = null;
        var membership = await _db.StaffMemberships
            .IgnoreQueryFilters() // cross-business by design: scoped to this user's id
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.IsActive)
            .OrderBy(m => m.JoinedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            return AuthResult.Fail("No business membership found for this account.", "NO_MEMBERSHIP");
        }

        var business = await _db.Businesses.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == membership.BusinessId, cancellationToken);
        if (business is null)
        {
            return AuthResult.Fail("Business not found.", "BUSINESS_MISSING");
        }

        return await IssueTokensAsync(user, business, membership.Role, ipAddress, cancellationToken);
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
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            await RevokeAllRefreshTokensForUserAsync(userId, ipAddress, cancellationToken);
        }
    }

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
}
