using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Web.Services;

public static class AuthCookieSignIn
{
    public const string CookieScheme = "NeedleBaseAuth";

    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    public static async Task SignInAsync(
        HttpContext httpContext,
        AuthSessionResult session,
        bool rememberMe,
        ILogger? logger = null)
    {
        var user = session.User;
        var tokens = session.Tokens;
        var roles = ExtractRoles(tokens.AccessToken);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(ClaimTypes.Name, user.Email),
            new(ClaimTypes.Email, user.Email),
            new("AccessToken", tokens.AccessToken),
            new("RefreshToken", tokens.RefreshToken),
            new("BusinessId", user.BusinessId),
            new("BusinessName", user.BusinessName),
            new("FirstName", user.FirstName),
            new("Surname", user.Surname)
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (!roles.Contains(user.Role, StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, user.Role));
        }

        var identity = new ClaimsIdentity(claims, CookieScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe
                ? DateTimeOffset.UtcNow.AddDays(30)
                : DateTimeOffset.UtcNow.AddHours(8),
            AllowRefresh = true,
            IssuedUtc = DateTimeOffset.UtcNow
        };

        // Drop any prior process-cached tokens for this user before the new session is used.
        TokenRefreshHandler.ClearCachedTokens(user.UserId);
        TokenRefreshHandler.ClearCachedTokens(user.Email);

        await httpContext.SignInAsync(CookieScheme, principal, authProperties);
        httpContext.User = principal;

        logger?.LogInformation("Signed in {Email} for business {BusinessName}", user.Email, user.BusinessName);
    }

    private static IReadOnlyList<string> ExtractRoles(string accessToken)
    {
        try
        {
            var token = accessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? accessToken[7..]
                : accessToken;

            if (!TokenHandler.CanReadToken(token))
            {
                return Array.Empty<string>();
            }

            var jwt = TokenHandler.ReadJwtToken(token);
            return jwt.Claims
                .Where(c => c.Type is ClaimTypes.Role or "role")
                .Select(c => c.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
