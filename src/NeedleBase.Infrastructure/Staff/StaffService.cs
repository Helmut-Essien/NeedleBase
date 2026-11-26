using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Email;
using NeedleBase.Application.Licensing;
using NeedleBase.Application.Staff;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Identity;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Staff;

/// <summary>
/// Staff screen: members, invites, role and access changes for the caller's business (tenant-filtered).
/// Role/access changes rotate the member's security stamp, so their current access token stops working at once
/// and the next refresh carries the new role (or NO_MEMBERSHIP, which wipes the phone).
/// </summary>
public sealed class StaffService : IStaffService
{
    /// <summary>Open invites per business; stops a runaway loop filling the table.</summary>
    public const int MaxPendingInvites = 20;

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;
    private readonly IEmailSender _email;
    private readonly IOptionsMonitor<Email.EmailOptions> _emailOptions;
    private readonly Licensing.LicenseService _licenses;
    private readonly IPlanEmailNotifier _planEmails;
    private readonly ILogger<StaffService> _logger;

    /// <summary>Most orders moved in one reassignment (one transaction); the Owner can repeat for the rest.</summary>
    public const int MaxReassign = 2000;

    public StaffService(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ICurrentUser currentUser, IClock clock, IAuditLog audit,
        IEmailSender email, IOptionsMonitor<Email.EmailOptions> emailOptions, Licensing.LicenseService licenses,
        IPlanEmailNotifier planEmails, ILogger<StaffService> logger)
    {
        _email = email;
        _emailOptions = emailOptions;
        _licenses = licenses;
        _planEmails = planEmails;
        _db = db;
        _userManager = userManager;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _logger = logger;
    }

    public async Task<StaffOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var members = await (
                from m in _db.StaffMemberships.AsNoTracking()
                join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                orderby m.JoinedAtUtc, m.UserId
                select new { m.UserId, u.FirstName, u.Surname, u.Email, m.Role, m.IsActive, m.JoinedAtUtc })
            .Take(500)
            .ToListAsync(cancellationToken);

        // Open work per assignee (orders are tenant-filtered).
        var counts = await _db.Orders.AsNoTracking()
            .Where(o => o.AssignedToUserId != null &&
                        o.Status != OrderStatus.Collected && o.Status != OrderStatus.Cancelled)
            .GroupBy(o => o.AssignedToUserId!)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        var invites = await _db.Invitations.AsNoTracking()
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var memberDtos = members
            .OrderByDescending(m => m.Role == BusinessRole.Owner)
            .ThenByDescending(m => m.IsActive)
            .ThenBy(m => m.FirstName).ThenBy(m => m.Surname)
            .Select(m => new StaffMemberDto(m.UserId, StaffNameDto.Compose(m.FirstName, m.Surname), m.Email ?? string.Empty, m.Role,
                m.IsActive, m.JoinedAtUtc, counts.GetValueOrDefault(m.UserId), m.UserId == _currentUser.UserId))
            .ToList();

        // Pending first, then the rest newest first; old revoked/expired ones fall off after 30 days.
        var inviteDtos = invites
            .Select(i => ToDto(i, now))
            .Where(i => i.Status is InvitationStatus.Pending or InvitationStatus.Accepted || now - i.CreatedAtUtc < TimeSpan.FromDays(30))
            .OrderByDescending(i => i.Status == InvitationStatus.Pending)
            .ThenByDescending(i => i.CreatedAtUtc)
            .ToList();

        return new StaffOverviewDto(memberDtos, inviteDtos, StaffRoles.CanManage(_currentUser.Role));
    }

    public async Task<StaffResult<CreatedInvitationDto>> InviteAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default)
    {
        var businessId = RequireBusiness();
        var role = request.Role?.Trim();
        if (!StaffRoles.IsAssignable(role))
        {
            return StaffResult<CreatedInvitationDto>.Fail(StaffErrorMessages.RoleInvalid);
        }

        var email = request.Email?.Trim() ?? string.Empty;
        if (email.Length is 0 or > Invitation.EmailMax || !IsPlainEmail(email) ||
            request.Name?.Any(char.IsControl) == true ||
            request.Name?.Trim() is { Length: > Invitation.NameMax })
        {
            return StaffResult<CreatedInvitationDto>.Fail(StaffErrorMessages.Validation);
        }

        // One account belongs to one business (sign-in picks the first membership), so an existing email can't join.
        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            return StaffResult<CreatedInvitationDto>.Fail(StaffErrorMessages.InviteEmailInUse);
        }

        var now = _clock.UtcNow;
        var normalized = email.ToUpperInvariant();
        string? failCode = null;
        Invitation? invite = null;
        string? code = null;
        string businessName = "Your shop";

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            failCode = null;
            invite = null;
            code = null;
            _db.ChangeTracker.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Serialize invite vs accept vs reactivate against the plan staff cap.
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"Businesses\" WHERE \"Id\" = {businessId} FOR UPDATE", cancellationToken);
                var business = await _db.Businesses.AsNoTracking().FirstAsync(b => b.Id == businessId, cancellationToken);
                businessName = business.Name;
                var resolution = PlanCatalog.Resolve(business);
                LogUnknownPlan(business.Id, resolution);

                var open = await _db.Invitations
                    .Where(i => i.AcceptedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > now)
                    .ToListAsync(cancellationToken);

                // Inviting the same email again replaces the old code (lost message, wrong role).
                foreach (var same in open.Where(i => i.Email.ToUpperInvariant() == normalized))
                {
                    same.Revoke(now);
                }

                var pendingOthers = open.Count(i => i.RevokedAtUtc is null);
                if (pendingOthers >= MaxPendingInvites)
                {
                    failCode = StaffErrorMessages.InviteLimit;
                    await tx.RollbackAsync(cancellationToken);
                    return;
                }

                var activeNonOwner = await CountActiveNonOwnerAsync(businessId, cancellationToken);
                if (!StaffPlanLimits.CanAddOne(resolution.Entitlements, activeNonOwner, pendingOthers))
                {
                    failCode = StaffErrorMessages.StaffLimit;
                    await tx.RollbackAsync(cancellationToken);
                    return;
                }

                code = InviteCodes.NewCode();
                invite = Invitation.Create(IdGenerator.NewId(), businessId, email, request.Name, role!,
                    InviteCodes.Hash(code)!, _currentUser.UserId!, now);
                _db.Invitations.Add(invite);
                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });

        if (failCode is not null)
        {
            return StaffResult<CreatedInvitationDto>.Fail(failCode);
        }

        await _audit.WriteAsync(new AuditEntry(AuditActions.StaffInvited, $"Invited someone as {role} ({AuditMask.Email(email)})",
            businessId, _currentUser.UserId, _currentUser.Role, "staff_invite", invite!.Id), cancellationToken: cancellationToken);
        var emailed = false;
        if (_email.IsConfigured)
        {
            emailed = _email.TryQueue(EmailTemplates.Invite(invite.Email, invite.Name, businessName, invite.Role, code!,
                invite.ExpiresAtUtc, _emailOptions.CurrentValue.AppBaseUrl));
        }

        _licenses.Invalidate(businessId);
        return StaffResult<CreatedInvitationDto>.Ok(new CreatedInvitationDto(ToDto(invite, now), code!, emailed));
    }

    public async Task<StaffResult<bool>> RevokeInvitationAsync(string invitationId, CancellationToken cancellationToken = default)
    {
        var businessId = RequireBusiness();
        var invite = await _db.Invitations.FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invite is null)
        {
            return StaffResult<bool>.Fail(StaffErrorMessages.InviteNotFound);
        }

        if (invite.AcceptedAtUtc is not null)
        {
            return StaffResult<bool>.Fail(StaffErrorMessages.InviteUsed);
        }

        if (invite.RevokedAtUtc is null)
        {
            invite.Revoke(_clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.WriteAsync(new AuditEntry(AuditActions.StaffInviteRevoked, "Cancelled an invite",
                businessId, _currentUser.UserId, _currentUser.Role, "staff_invite", invite.Id), cancellationToken: cancellationToken);
            _licenses.Invalidate(businessId);
        }

        return StaffResult<bool>.Ok(true);
    }

    public Task<StaffResult<StaffMemberDto>> ChangeRoleAsync(string userId, string role, CancellationToken cancellationToken = default) =>
        ApplyMemberChangeAsync(userId, m =>
        {
            if (!StaffRoles.IsAssignable(role?.Trim()))
            {
                return StaffErrorMessages.RoleInvalid;
            }

            if (m.Role == role!.Trim())
            {
                return null;
            }

            m.ChangeRole(role.Trim());
            return null;
        }, cancellationToken);

    public Task<StaffResult<StaffMemberDto>> SetActiveAsync(string userId, bool active, CancellationToken cancellationToken = default) =>
        active
            ? ReactivateUnderPlanCapAsync(userId, cancellationToken)
            : ApplyMemberChangeAsync(userId, m =>
            {
                m.Deactivate();
                return null;
            }, cancellationToken);

    /// <summary>Reactivate under the business row lock so two reactivations can't pass the plan staff cap together.</summary>
    private async Task<StaffResult<StaffMemberDto>> ReactivateUnderPlanCapAsync(string userId, CancellationToken cancellationToken)
    {
        var businessId = RequireBusiness();
        if (string.Equals(userId, _currentUser.UserId, StringComparison.Ordinal))
        {
            return StaffResult<StaffMemberDto>.Fail(StaffErrorMessages.StaffSelf);
        }

        string? failCode = null;
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            failCode = null;
            _db.ChangeTracker.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"Businesses\" WHERE \"Id\" = {businessId} FOR UPDATE", cancellationToken);
                var business = await _db.Businesses.AsNoTracking().FirstAsync(b => b.Id == businessId, cancellationToken);
                var resolution = PlanCatalog.Resolve(business);
                LogUnknownPlan(business.Id, resolution);

                var membership = await _db.StaffMemberships.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
                if (membership is null)
                {
                    failCode = StaffErrorMessages.StaffNotFound;
                    await tx.RollbackAsync(cancellationToken);
                    return;
                }

                if (membership.Role == BusinessRole.Owner)
                {
                    failCode = StaffErrorMessages.StaffOwner;
                    await tx.RollbackAsync(cancellationToken);
                    return;
                }

                if (!membership.IsActive)
                {
                    var activeNonOwner = await CountActiveNonOwnerAsync(businessId, cancellationToken);
                    var pending = await CountPendingInvitesAsync(businessId, _clock.UtcNow, cancellationToken);
                    if (!StaffPlanLimits.CanAddOne(resolution.Entitlements, activeNonOwner, pending))
                    {
                        failCode = StaffErrorMessages.StaffLimit;
                        await tx.RollbackAsync(cancellationToken);
                        return;
                    }

                    membership.Reactivate();
                    var user = await _userManager.FindByIdAsync(userId);
                    if (user is null)
                    {
                        failCode = StaffErrorMessages.StaffNotFound;
                        await tx.RollbackAsync(cancellationToken);
                        return;
                    }

                    var stamped = await _userManager.UpdateSecurityStampAsync(user);
                    if (!stamped.Succeeded)
                    {
                        throw new InvalidOperationException("Couldn't update the team member's sign-in.");
                    }
                }

                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });

        if (failCode is not null)
        {
            return StaffResult<StaffMemberDto>.Fail(failCode);
        }

        _licenses.Invalidate(businessId);
        var overview = await GetOverviewAsync(cancellationToken);
        return StaffResult<StaffMemberDto>.Ok(overview.Members.First(m => m.UserId == userId));
    }

    private async Task<StaffResult<StaffMemberDto>> ApplyMemberChangeAsync(
        string userId, Func<StaffMembership, string?> change, CancellationToken cancellationToken)
    {
        var businessId = RequireBusiness();
        if (string.Equals(userId, _currentUser.UserId, StringComparison.Ordinal))
        {
            return StaffResult<StaffMemberDto>.Fail(StaffErrorMessages.StaffSelf);
        }

        var membership = await _db.StaffMemberships.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (membership is null)
        {
            return StaffResult<StaffMemberDto>.Fail(StaffErrorMessages.StaffNotFound);
        }

        if (membership.Role == BusinessRole.Owner)
        {
            return StaffResult<StaffMemberDto>.Fail(StaffErrorMessages.StaffOwner);
        }

        var (oldRole, oldActive) = (membership.Role, membership.IsActive);
        if (change(membership) is { } error)
        {
            return StaffResult<StaffMemberDto>.Fail(error);
        }

        if (membership.Role != oldRole || membership.IsActive != oldActive)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return StaffResult<StaffMemberDto>.Fail(StaffErrorMessages.StaffNotFound);
            }

            // One save: the membership change and the new stamp land together (UserManager saves the shared context).
            var stamped = await _userManager.UpdateSecurityStampAsync(user);
            if (!stamped.Succeeded)
            {
                throw new InvalidOperationException("Couldn't update the team member's sign-in.");
            }

            if (membership.IsActive != oldActive)
            {
                _licenses.Invalidate(businessId);
            }
        }

        var overview = await GetOverviewAsync(cancellationToken);
        return StaffResult<StaffMemberDto>.Ok(overview.Members.First(m => m.UserId == userId));
    }

    public async Task<StaffResult<ReassignOrdersResultDto>> ReassignOpenOrdersAsync(
        string fromUserId, string? toUserId, CancellationToken cancellationToken = default)
    {
        var businessId = RequireBusiness();
        toUserId = string.IsNullOrWhiteSpace(toUserId) ? null : toUserId.Trim();
        // Members of this business only (tenant filter); the source may be turned off, the target must be active.
        if (!await _db.StaffMemberships.AnyAsync(m => m.UserId == fromUserId, cancellationToken))
        {
            return StaffResult<ReassignOrdersResultDto>.Fail(StaffErrorMessages.StaffNotFound);
        }

        string? targetName = null;
        if (toUserId is not null)
        {
            var target = await (
                    from m in _db.StaffMemberships.AsNoTracking().Where(m => m.UserId == toUserId && m.IsActive)
                    join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                    select new { u.FirstName, u.Surname })
                .FirstOrDefaultAsync(cancellationToken);
            if (target is null || toUserId == fromUserId)
            {
                return StaffResult<ReassignOrdersResultDto>.Fail(StaffErrorMessages.ReassignTargetInvalid);
            }

            targetName = StaffNameDto.Compose(target.FirstName, target.Surname);
        }

        var now = _clock.UtcNow;
        var moved = 0;
        var targetGone = false;
        var movedSnapshot = new List<(string Id, string DisplayRef, DateOnly? DueDate, string CustomerId)>();
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            moved = 0;
            targetGone = false;
            movedSnapshot = [];
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            if (toUserId is not null)
            {
                // Lock the target's membership: a "turn off" racing this move waits, and a target turned off since the
                // check above is caught here, so no orders land on someone who can't work them.
                var stillActive = await _db.Database.SqlQuery<int>(
                        $"SELECT 1 AS \"Value\" FROM \"StaffMemberships\" WHERE \"BusinessId\" = {businessId} AND \"UserId\" = {toUserId} AND \"IsActive\" FOR UPDATE")
                    .ToListAsync(cancellationToken);
                if (stillActive.Count == 0)
                {
                    targetGone = true;
                    return;
                }
            }

            // Header only: lines aren't loaded or touched. Each order's DateModifiedUtc moves, so a phone's offline edit
            // based on the old version conflicts (no silent overwrite) and the next pull brings the new assignee.
            // Row-lock the open orders first (same order as the load: no deadlock between two moves). An edit that
            // committed meanwhile is re-checked by Postgres, so an order someone just gave to another person is
            // skipped instead of being silently moved; an edit still in flight waits, then fails as STALE_UPDATE.
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Orders\" WHERE \"BusinessId\" = {businessId} AND \"AssignedToUserId\" = {fromUserId} AND \"Status\" <> {OrderStatus.Collected} AND \"Status\" <> {OrderStatus.Cancelled} ORDER BY \"Id\" LIMIT {MaxReassign} FOR UPDATE",
                cancellationToken);
            var orders = await _db.Orders
                .Where(o => o.AssignedToUserId == fromUserId && o.Status != OrderStatus.Collected && o.Status != OrderStatus.Cancelled)
                .OrderBy(o => o.Id)
                .Take(MaxReassign)
                .ToListAsync(cancellationToken);
            foreach (var order in orders)
            {
                order.Reassign(toUserId, now);
            }

            moved = orders.Count;
            movedSnapshot = orders
                .Select(o => (o.Id, o.DisplayRef, o.DueDate, o.CustomerId))
                .ToList();
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });

        if (targetGone)
        {
            return StaffResult<ReassignOrdersResultDto>.Fail(StaffErrorMessages.ReassignTargetInvalid);
        }

        if (moved > 0)
        {
            var fromName = await (
                    from u in _db.Users.AsNoTracking().Where(u => u.Id == fromUserId)
                    select new { u.FirstName, u.Surname })
                .FirstOrDefaultAsync(cancellationToken);
            var who = fromName is null ? "a team member" : StaffNameDto.Compose(fromName.FirstName, fromName.Surname);
            var summary = targetName is null
                ? $"Unassigned {moved} open order{(moved == 1 ? "" : "s")} from {who}"
                : $"Moved {moved} open order{(moved == 1 ? "" : "s")} from {who} to {targetName}";
            await _audit.WriteAsync(new AuditEntry(AuditActions.StaffOrdersReassigned, summary,
                businessId, _currentUser.UserId, _currentUser.Role, "staff", fromUserId), cancellationToken: cancellationToken);

            if (toUserId is not null && movedSnapshot.Count > 0)
            {
                var customerIds = movedSnapshot.Select(o => o.CustomerId).Distinct().ToList();
                var names = await _db.Customers.AsNoTracking()
                    .Where(c => customerIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
                // One digest (or single) mail — never one queue entry per order (SMTP channel capacity is 200).
                var assigned = movedSnapshot
                    .Select(o => new PlanEmailAssignedOrder(
                        o.Id, o.DisplayRef, o.DueDate,
                        names.TryGetValue(o.CustomerId, out var n) ? n : "a customer"))
                    .ToList();
                await _planEmails.NotifyBulkAssignmentAsync(
                    businessId, previousAssigneeUserId: fromUserId, assigneeUserId: toUserId, assigned, cancellationToken);
            }
        }

        return StaffResult<ReassignOrdersResultDto>.Ok(new ReassignOrdersResultDto(moved, toUserId, targetName));
    }

    private string RequireBusiness() =>
        string.IsNullOrEmpty(_currentUser.BusinessId)
            ? throw new InvalidOperationException("No business on this request.")
            : _currentUser.BusinessId;

    private Task<int> CountActiveNonOwnerAsync(string businessId, CancellationToken cancellationToken) =>
        _db.StaffMemberships.CountAsync(
            m => m.BusinessId == businessId && m.IsActive && m.Role != BusinessRole.Owner,
            cancellationToken);

    private Task<int> CountPendingInvitesAsync(string businessId, DateTimeOffset now, CancellationToken cancellationToken) =>
        _db.Invitations.CountAsync(
            i => i.BusinessId == businessId && i.AcceptedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > now,
            cancellationToken);

    private void LogUnknownPlan(string businessId, PlanResolution resolution)
    {
        if (!resolution.WasUnknownPlan)
        {
            return;
        }

        _logger.LogWarning(
            "Shop {ShopId} has unexpected license plan {Plan}; applying Starter staff limits.",
            businessId,
            resolution.StoredPlan ?? "(blank)");
    }

    private static InvitationDto ToDto(Invitation i, DateTimeOffset now) =>
        new(i.Id, i.Email, i.Name, i.Role, i.StatusAt(now), i.CreatedAtUtc, i.ExpiresAtUtc, i.AcceptedAtUtc);

    /// <summary>One plain address (no "Name &lt;a@b&gt;", lists, spaces or control characters).</summary>
    internal static bool IsPlainEmail(string email)
    {
        if (email.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) || email.IndexOf('@') is < 1 || email.EndsWith('@'))
        {
            return false;
        }

        try
        {
            return string.Equals(new System.Net.Mail.MailAddress(email).Address, email, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
