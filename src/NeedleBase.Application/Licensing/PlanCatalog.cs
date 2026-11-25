using NeedleBase.Domain.Entities;

namespace NeedleBase.Application.Licensing;

/// <summary>Stable Platform plan codes NeedleBase recognises. Free never touches Platform.</summary>
public static class PlanCodes
{
    public const string Starter = "starter";
    public const string Workshop = "workshop";
    public const string Production = "production";

    /// <summary>Keyless or complimentary shops — not a Platform plan code.</summary>
    public const string Unlimited = "unlimited";
}

/// <summary>
/// What a resolved plan allows. Staff caps exclude the Owner. Customer/order caps are Free-only
/// (phone local) and are not modelled here. Enforcement of these limits lands in later plan steps.
/// </summary>
public sealed record PlanEntitlements
{
    public required string Code { get; init; }

    /// <summary>No staff or feature caps (keyless shops and complimentary keys).</summary>
    public bool IsUnlimited { get; init; }

    /// <summary>Max staff excluding the Owner; null when <see cref="IsUnlimited"/>.</summary>
    public int? MaxStaffExcludingOwner { get; init; }

    public bool Sync { get; init; }
    public bool Website { get; init; }
    public bool Export { get; init; }
    public bool CloudBackup { get; init; }
    public bool ShopPhoneOnReceipts { get; init; }
    public bool MonthlySummaryEmail { get; init; }
    public bool WeeklySummaryEmail { get; init; }
    public bool AssignmentAndDueEmails { get; init; }
    public bool Photos { get; init; }
    public bool PdfReceipts { get; init; }
    public bool ProfitAndMonthlyReports { get; init; }

    /// <summary>Production only: higher photo storage than Workshop.</summary>
    public bool ExtraPhotoStorage { get; init; }
}

/// <summary>
/// Result of resolving a shop's licence fields to entitlements.
/// When <see cref="WasUnknownPlan"/> is true, callers should log <see cref="StoredPlan"/>.
/// </summary>
public sealed record PlanResolution(PlanEntitlements Entitlements, string? StoredPlan, bool WasUnknownPlan);

/// <summary>
/// Maps a Platform plan code (and licence flags) to NeedleBase limits and features.
/// Staff caps are enforced via <see cref="Staff.StaffPlanLimits"/> on invite/accept/reactivate.
/// Free mode is a later step.
/// </summary>
public static class PlanCatalog
{
    private static readonly PlanEntitlements Starter = Paid(
        PlanCodes.Starter,
        maxStaff: 2,
        monthlyEmail: true);

    private static readonly PlanEntitlements Workshop = Paid(
        PlanCodes.Workshop,
        maxStaff: 8,
        monthlyEmail: true,
        weeklyEmail: true,
        assignmentEmails: true,
        photos: true,
        pdfReceipts: true,
        reports: true);

    private static readonly PlanEntitlements Production = Paid(
        PlanCodes.Production,
        maxStaff: 20,
        monthlyEmail: true,
        weeklyEmail: true,
        assignmentEmails: true,
        photos: true,
        pdfReceipts: true,
        reports: true,
        extraPhotos: true);

    private static readonly PlanEntitlements Unlimited = new()
    {
        Code = PlanCodes.Unlimited,
        IsUnlimited = true,
        MaxStaffExcludingOwner = null,
        Sync = true,
        Website = true,
        Export = true,
        CloudBackup = true,
        ShopPhoneOnReceipts = true,
        MonthlySummaryEmail = true,
        WeeklySummaryEmail = true,
        AssignmentAndDueEmails = true,
        Photos = true,
        PdfReceipts = true,
        ProfitAndMonthlyReports = true,
        ExtraPhotoStorage = true
    };

    /// <summary>Known paid plan for a normalised code, or null if blank/unknown.</summary>
    public static PlanEntitlements? ForCode(string? planCode)
    {
        var code = Normalise(planCode);
        return code switch
        {
            PlanCodes.Starter => Starter,
            PlanCodes.Workshop => Workshop,
            PlanCodes.Production => Production,
            _ => null
        };
    }

    /// <summary>
    /// Resolve entitlements from a shop's cached licence fields.
    /// Keyless → unlimited; complimentary → unlimited; trial → Starter; unknown/blank on a keyed shop → Starter (fail closed).
    /// </summary>
    public static PlanResolution Resolve(Business business)
    {
        ArgumentNullException.ThrowIfNull(business);
        return Resolve(
            business.LicenseKeyProtected,
            business.LicensePlan,
            business.LicenseIsTrial,
            business.LicenseIsComplimentary);
    }

    /// <summary>Same rules as <see cref="Resolve(Business)"/> without needing a <see cref="Business"/> instance.</summary>
    public static PlanResolution Resolve(
        string? licenseKeyProtected,
        string? plan,
        bool isTrial,
        bool isComplimentary)
    {
        var stored = string.IsNullOrWhiteSpace(plan) ? null : plan.Trim();

        if (string.IsNullOrEmpty(licenseKeyProtected))
        {
            return new PlanResolution(Unlimited, stored, WasUnknownPlan: false);
        }

        if (isComplimentary)
        {
            return new PlanResolution(Unlimited, stored, WasUnknownPlan: false);
        }

        if (isTrial)
        {
            return new PlanResolution(Starter, stored, WasUnknownPlan: false);
        }

        var known = ForCode(plan);
        if (known is not null)
        {
            return new PlanResolution(known, stored, WasUnknownPlan: false);
        }

        // Licensed shop with blank or unexpected plan: never unlimited.
        return new PlanResolution(Starter, stored, WasUnknownPlan: true);
    }

    private static string? Normalise(string? planCode)
    {
        if (string.IsNullOrWhiteSpace(planCode))
        {
            return null;
        }

        return planCode.Trim().ToLowerInvariant();
    }

    private static PlanEntitlements Paid(
        string code,
        int maxStaff,
        bool monthlyEmail = false,
        bool weeklyEmail = false,
        bool assignmentEmails = false,
        bool photos = false,
        bool pdfReceipts = false,
        bool reports = false,
        bool extraPhotos = false) =>
        new()
        {
            Code = code,
            IsUnlimited = false,
            MaxStaffExcludingOwner = maxStaff,
            Sync = true,
            Website = true,
            Export = true,
            CloudBackup = true,
            ShopPhoneOnReceipts = true,
            MonthlySummaryEmail = monthlyEmail,
            WeeklySummaryEmail = weeklyEmail,
            AssignmentAndDueEmails = assignmentEmails,
            Photos = photos,
            PdfReceipts = pdfReceipts,
            ProfitAndMonthlyReports = reports,
            ExtraPhotoStorage = extraPhotos
        };
}
