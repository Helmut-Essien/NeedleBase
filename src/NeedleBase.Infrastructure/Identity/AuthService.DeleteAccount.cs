using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Staff;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Identity;

public sealed partial class AuthService
{
    public async Task<AuthResult> DeleteAccountAsync(
        string userId, string? businessId, DeleteAccountRequest request, string? ipAddress, CancellationToken cancellationToken = default)
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

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            _throttle.RecordFailure(email, ipAddress);
            return AuthResult.Fail(StaffErrorMessages.For(StaffErrorMessages.PasswordIncorrect), StaffErrorMessages.PasswordIncorrect);
        }

        _throttle.RecordSuccess(email, ipAddress);

        if (string.IsNullOrEmpty(businessId))
        {
            return AuthResult.Fail("No business membership found.", "NO_MEMBERSHIP");
        }

        _db.CurrentBusinessId = null;
        var businessInfo = await _db.Businesses.AsNoTracking()
            .Where(b => b.Id == businessId)
            .Select(b => new { Closed = b.ClosedAtUtc != null })
            .FirstOrDefaultAsync(cancellationToken);
        if (businessInfo is null)
        {
            return AuthResult.Fail("Business not found.", "BUSINESS_MISSING");
        }

        if (businessInfo.Closed)
        {
            return AuthResult.Fail("This shop account has been deleted.", "SHOP_CLOSED");
        }

        var membershipInfo = await _db.StaffMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == user.Id && m.BusinessId == businessId)
            .Select(m => new { m.Id, m.Role, m.IsActive })
            .FirstOrDefaultAsync(cancellationToken);
        if (membershipInfo is null)
        {
            return AuthResult.Fail("No business membership found.", "NO_MEMBERSHIP");
        }

        if (!membershipInfo.IsActive)
        {
            return AuthResult.Fail("Account deactivated.", "ACCOUNT_DEACTIVATED");
        }

        var confirmation = request.Confirmation ?? string.Empty;
        var isOwner = membershipInfo.Role == BusinessRole.Owner;
        if (isOwner)
        {
            if (!DeleteAccountConfirmations.Matches(confirmation, DeleteAccountConfirmations.OwnerShop))
            {
                return AuthResult.Fail(
                    "To close the shop, type DELETE SHOP to confirm.",
                    DeleteAccountConfirmations.Matches(confirmation, DeleteAccountConfirmations.Staff)
                        ? "OWNER_CONFIRMATION_REQUIRED"
                        : "DELETE_CONFIRMATION_REQUIRED");
            }
        }
        else if (!DeleteAccountConfirmations.Matches(confirmation, DeleteAccountConfirmations.Staff)
                 && !DeleteAccountConfirmations.Matches(confirmation, DeleteAccountConfirmations.OwnerShop))
        {
            return AuthResult.Fail("Type DELETE to confirm you want to delete your account.", "DELETE_CONFIRMATION_REQUIRED");
        }

        var actorId = user.Id;
        var actorRole = membershipInfo.Role;
        var membershipId = membershipInfo.Id;
        string? abortCode = null;

        var previousTimeout = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(120);
        bool committed;
        try
        {
            committed = await _db.InTransactionAsync(async ct =>
            {
                // InTransactionAsync clears the change tracker first, so entities must be loaded inside it.
                // Lock the shop row so a second delete, a sync push, or another writer waits for this close/purge.
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"Businesses\" WHERE \"Id\" = {businessId} FOR UPDATE", ct);

                var membership = await _db.StaffMemberships.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.Id == membershipId, ct);
                var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, ct);
                if (business is null)
                {
                    abortCode = "BUSINESS_MISSING";
                    return (false, false);
                }

                if (business.IsClosed)
                {
                    abortCode = "SHOP_CLOSED";
                    return (false, false);
                }

                if (membership is null || !membership.IsActive)
                {
                    abortCode = "NO_MEMBERSHIP";
                    return (false, false);
                }

                var actor = await _userManager.FindByIdAsync(actorId);
                if (actor is null || !actor.IsActive)
                {
                    abortCode = "ACCOUNT_DEACTIVATED";
                    return (false, false);
                }

                if (isOwner)
                {
                    await CloseShopAsync(business, ct);
                }
                else
                {
                    membership.Deactivate();
                    if (!await HasOtherActiveMembershipAsync(actor.Id, businessId, ct))
                    {
                        await AnonymiseUserAsync(actor, ct);
                    }

                    await _db.SaveChangesAsync(ct);
                }

                return (true, true);
            }, cancellationToken);
        }
        finally
        {
            _db.Database.SetCommandTimeout(previousTimeout);
        }

        if (!committed)
        {
            return abortCode switch
            {
                "SHOP_CLOSED" => AuthResult.Fail("This shop account has been deleted.", "SHOP_CLOSED"),
                "ACCOUNT_DEACTIVATED" => AuthResult.Fail("Account deactivated.", "ACCOUNT_DEACTIVATED"),
                "BUSINESS_MISSING" => AuthResult.Fail("Business not found.", "BUSINESS_MISSING"),
                "NO_MEMBERSHIP" => AuthResult.Fail("No business membership found.", "NO_MEMBERSHIP"),
                _ => AuthResult.Fail("Couldn't delete the account. Try again.")
            };
        }

        // Audit writes use their own short transaction and must not run inside the caller's unit of work.
        if (isOwner)
        {
            await _audit.WriteAsync(new AuditEntry(
                AuditActions.BusinessClosed,
                "Shop account closed; hosted shop records deleted",
                businessId, actorId, actorRole), cancellationToken: cancellationToken);
            await _audit.WriteAsync(new AuditEntry(
                AuditActions.AccountDeleted,
                "Owner account deleted and shop closed",
                businessId, actorId, actorRole), cancellationToken: cancellationToken);
        }
        else
        {
            await _audit.WriteAsync(new AuditEntry(
                AuditActions.AccountDeleted,
                "Staff sign-in account deleted",
                businessId, actorId, actorRole), cancellationToken: cancellationToken);
        }

        return AuthResult.Done();
    }

    private async Task CloseShopAsync(Business business, CancellationToken cancellationToken)
    {
        var businessId = business.Id;
        business.Close(_clock.UtcNow);

        var members = await _db.StaffMemberships.IgnoreQueryFilters()
            .Where(m => m.BusinessId == businessId)
            .ToListAsync(cancellationToken);
        var userIds = members.Select(m => m.UserId).Distinct().ToList();
        foreach (var member in members)
        {
            member.CloseWithShop();
        }

        var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToListAsync(cancellationToken);
        foreach (var shopUser in users)
        {
            // A person who also belongs to another open shop keeps that sign-in; only this shop is closed.
            if (!await HasOtherActiveMembershipAsync(shopUser.Id, businessId, cancellationToken))
            {
                await AnonymiseUserAsync(shopUser, cancellationToken);
            }
        }

        await PurgeShopRecordsAsync(businessId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private Task<bool> HasOtherActiveMembershipAsync(string userId, string businessId, CancellationToken cancellationToken) =>
        _db.StaffMemberships.IgnoreQueryFilters()
            .AnyAsync(m => m.UserId == userId && m.BusinessId != businessId && m.IsActive, cancellationToken);

    private async Task AnonymiseUserAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.IsActive = false;
        user.FirstName = "Deleted";
        user.Surname = "User";
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        user.EmailConfirmed = false;
        user.Email = $"deleted.{user.Id}@invalid.local";
        user.NormalizedEmail = _userManager.NormalizeEmail(user.Email);
        user.UserName = user.Email;
        user.NormalizedUserName = _userManager.NormalizeName(user.UserName);
        var updated = await _userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException(
                "Couldn't anonymise the account: " + string.Join("; ", updated.Errors.Select(e => e.Description)));
        }

        if (await _userManager.HasPasswordAsync(user))
        {
            var removed = await _userManager.RemovePasswordAsync(user);
            if (!removed.Succeeded)
            {
                throw new InvalidOperationException("Couldn't remove the account password.");
            }
        }

        var stamped = await _userManager.UpdateSecurityStampAsync(user);
        if (!stamped.Succeeded)
        {
            throw new InvalidOperationException("Couldn't sign the account out of other devices.");
        }

        await _db.RefreshTokens.Where(t => t.ApplicationUserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await _db.PasswordResetCodes.Where(c => c.ApplicationUserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await _db.Set<IdentityUserToken<string>>().Where(t => t.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await _db.Set<IdentityUserLogin<string>>().Where(t => t.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task PurgeShopRecordsAsync(string businessId, CancellationToken cancellationToken)
    {
        // Order respects foreign keys. ExecuteDelete is not intercepted, so no sync tombstones are written
        // for a shop that is already closed.
        await _db.Payments.IgnoreQueryFilters().Where(x => x.BusinessId == businessId).ExecuteDeleteAsync(cancellationToken);
        await _db.WhatsAppMessages.IgnoreQueryFilters().Where(x => x.BusinessId == businessId).ExecuteDeleteAsync(cancellationToken);
        await _db.OrderLines.IgnoreQueryFilters().Where(x => x.BusinessId == businessId).ExecuteDeleteAsync(cancellationToken);
        await _db.Orders.IgnoreQueryFilters().Where(x => x.BusinessId == businessId).ExecuteDeleteAsync(cancellationToken);
        await _db.WhatsAppConsents.IgnoreQueryFilters().Where(x => x.BusinessId == businessId).ExecuteDeleteAsync(cancellationToken);
        await _db.MeasurementProfiles.IgnoreQueryFilters().Where(x => x.BusinessId == businessId).ExecuteDeleteAsync(cancellationToken);
        await _db.Customers.IgnoreQueryFilters().Where(x => x.BusinessId == businessId).ExecuteDeleteAsync(cancellationToken);
        await _db.Invitations.IgnoreQueryFilters().Where(x => x.BusinessId == businessId).ExecuteDeleteAsync(cancellationToken);
        await _db.DeletedItems.IgnoreQueryFilters().Where(x => x.BusinessId == businessId).ExecuteDeleteAsync(cancellationToken);
        await _db.WhatsAppSettings.IgnoreQueryFilters().Where(x => x.BusinessId == businessId).ExecuteDeleteAsync(cancellationToken);
    }
}
