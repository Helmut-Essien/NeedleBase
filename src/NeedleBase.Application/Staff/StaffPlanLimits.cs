using NeedleBase.Application.Licensing;

namespace NeedleBase.Application.Staff;

/// <summary>
/// Plan staff caps (Owner not counted). Downgrade keeps existing staff; this only blocks
/// adding another seat (invite, accept, or reactivate) until the shop is under the cap.
/// </summary>
public static class StaffPlanLimits
{
    /// <summary>
    /// Whether one more non-Owner seat may be taken.
    /// <paramref name="reservedPendingInvites"/> counts open invites that will also become staff
    /// (use on invite/reactivate; use 0 on accept — that invite is converting, not an extra reservation).
    /// </summary>
    public static bool CanAddOne(PlanEntitlements entitlements, int activeNonOwnerStaff, int reservedPendingInvites = 0)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        if (entitlements.IsUnlimited || entitlements.MaxStaffExcludingOwner is not { } max)
        {
            return true;
        }

        if (activeNonOwnerStaff < 0 || reservedPendingInvites < 0)
        {
            return false;
        }

        return activeNonOwnerStaff + reservedPendingInvites < max;
    }
}
