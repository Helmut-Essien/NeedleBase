using Microsoft.AspNetCore.Identity;

namespace NeedleBase.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}
