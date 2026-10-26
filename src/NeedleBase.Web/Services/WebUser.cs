using System.Security.Claims;
using NeedleBase.Application.Payments;

namespace NeedleBase.Web.Services;

/// <summary>Cookie claims the pages need (role-aware nav, business name).</summary>
public static class WebUser
{
    public static string? Role(this ClaimsPrincipal user) => user.FindFirst(ClaimTypes.Role)?.Value;

    /// <summary>Owner/Manager see money (Tailors never do; the Api also returns 403 PAYMENT_FORBIDDEN).</summary>
    public static bool CanSeePayments(this ClaimsPrincipal user) => PaymentRules.CanManage(user.Role());

    /// <summary>Owner/Manager edit orders (due date, notes, garments and prices); the Api enforces it too.</summary>
    public static bool CanEditOrders(this ClaimsPrincipal user) => NeedleBase.Application.Orders.OrderEditor.CanEdit(user.Role());

    /// <summary>Owner/Manager see the activity log (the Api enforces it too; Managers without security events).</summary>
    public static bool CanSeeActivity(this ClaimsPrincipal user) =>
        user.Role() is "Owner" or "Manager";

    public static string BusinessName(this ClaimsPrincipal user) => user.FindFirst("BusinessName")?.Value ?? "NeedleBase";

    public static string DisplayName(this ClaimsPrincipal user)
    {
        var name = $"{user.FindFirst("FirstName")?.Value} {user.FindFirst("Surname")?.Value}".Trim();
        return name.Length > 0 ? name : user.Identity?.Name ?? string.Empty;
    }
}
