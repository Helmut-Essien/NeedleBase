using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;

namespace NeedleBase.Infrastructure.Auth;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? UserId =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue(JwtRegisteredClaimNamesSub);

    public string? BusinessId => Principal?.FindFirstValue(ClaimNames.BusinessId);

    public string? Role =>
        Principal?.FindFirstValue(ClaimNames.Role)
        ?? Principal?.FindFirstValue(ClaimTypes.Role);

    // Avoid taking a dependency on System.IdentityModel just for the constant name.
    private const string JwtRegisteredClaimNamesSub = "sub";
}
