namespace NeedleBase.Application.Contracts.Errors;

/// <summary>
/// Friendly, user-safe copy for auth failures, keyed by the API's stable ProblemDetails <c>code</c>.
/// Shared by mobile and web so both say the same thing. Unknown codes fall back to generic text.
/// </summary>
public static class AuthErrorMessages
{
    public const string LoginFallback = "Couldn't sign you in. Check your details and try again.";
    public const string RegisterFallback = "Couldn't create your account. Check your details and try again.";
    public const string ServerError = "Something went wrong on our side. Try again in a moment.";
    public const string RateLimited = "Too many attempts. Wait a minute, then try again.";

    public static string ForLogin(ApiProblem problem) =>
        Common(problem) ?? problem.Code switch
        {
            "INVALID_CREDENTIALS" => "Wrong email or password. Check them and try again.",
            _ => LoginFallback
        };

    public static string ForRegister(ApiProblem problem) =>
        Common(problem) ?? problem.Code switch
        {
            // The API deliberately does not say whether an email is already taken (no account enumeration).
            "REGISTER_FAILED" => "Couldn't create an account with those details. If you already have an account, sign in instead.",
            "VALIDATION_FAILED" => problem.FirstValidationError ?? "Check the details you entered and try again.",
            _ => RegisterFallback
        };

    private static string? Common(ApiProblem problem) => problem.Code switch
    {
        "ACCOUNT_LOCKED" or "LOGIN_THROTTLED" => "Too many wrong tries. Wait a few minutes, then try again.",
        "ACCOUNT_DEACTIVATED" => "This account has been turned off. Ask your business owner to turn it back on.",
        "NO_MEMBERSHIP" or "BUSINESS_MISSING" => "This account isn't linked to a business. Ask your business owner to add you.",
        "RATE_LIMITED" => RateLimited,
        _ when problem.Status == 429 => RateLimited,
        _ when problem.Status >= 500 => ServerError,
        _ => null
    };
}
