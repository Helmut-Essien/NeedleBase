namespace NeedleBase.Application.Contracts.Errors;

/// <summary>Stable codes + friendly copy for staff, invite and password-change failures (API detail, mobile and web).</summary>
public static class StaffErrorMessages
{
    public const string InviteNotFound = "INVITE_NOT_FOUND";
    public const string InviteExpired = "INVITE_EXPIRED";
    public const string InviteUsed = "INVITE_USED";
    public const string InviteRevoked = "INVITE_REVOKED";
    public const string InviteEmailInUse = "INVITE_EMAIL_IN_USE";
    public const string InviteLimit = "INVITE_LIMIT";
    public const string RoleInvalid = "STAFF_ROLE_INVALID";
    public const string StaffNotFound = "STAFF_NOT_FOUND";
    public const string StaffSelf = "STAFF_SELF";
    public const string StaffOwner = "STAFF_OWNER";
    public const string PasswordIncorrect = "PASSWORD_INCORRECT";
    public const string PasswordWeak = "PASSWORD_WEAK";
    public const string Validation = "VALIDATION_FAILED";
    public const string ResetCodeInvalid = "RESET_CODE_INVALID";
    public const string ResetUnavailable = "PASSWORD_RESET_UNAVAILABLE";
    public const string ReassignTargetInvalid = "REASSIGN_TARGET_INVALID";

    public const string Fallback = "Something went wrong. Check the details and try again.";

    public static string For(string? code) => code switch
    {
        InviteNotFound => "That invite code isn't right. Check it with the person who invited you.",
        InviteExpired => "This invite has expired. Ask the shop owner to send a new one.",
        InviteUsed => "This invite has already been used. Sign in instead.",
        InviteRevoked => "This invite was cancelled. Ask the shop owner for a new one.",
        InviteEmailInUse => "That email already has a NeedleBase account. Use a different email for this invite.",
        InviteLimit => "Too many open invites. Cancel some before sending more.",
        RoleInvalid => "Pick Manager or Tailor.",
        StaffNotFound => "We couldn't find that team member.",
        StaffSelf => "You can't change your own access.",
        StaffOwner => "The owner's access can't be changed.",
        PasswordIncorrect => "Your current password isn't right.",
        PasswordWeak => "Pick a stronger password: at least 6 characters, with a number and a small letter.",
        Validation => "Check the details and try again.",
        ResetCodeInvalid => "That code isn't right or has expired. Check the latest email, or ask for a new code.",
        ResetUnavailable => "Password reset by email isn't set up yet. Ask the shop owner for help.",
        ReassignTargetInvalid => "Pick an active team member (not the same person), or leave the orders unassigned.",
        _ => Fallback
    };

    /// <summary>Friendly copy for a failed staff / invite / password call (shared by web and mobile).</summary>
    public static string ForProblem(ApiProblem problem) => problem.Code switch
    {
        "LOGIN_THROTTLED" or "RATE_LIMITED" => "Too many tries. Wait a few minutes, then try again.",
        Validation => problem.FirstValidationError ?? For(problem.Code),
        _ when problem.Status == 429 => AuthErrorMessages.RateLimited,
        _ when problem.Status >= 500 => AuthErrorMessages.ServerError,
        _ when problem.Status == 403 && problem.Code is null => "Only the owner can change staff.",
        _ => problem.Code is null && problem.Detail is { Length: > 0 } d ? d : For(problem.Code)
    };
}
