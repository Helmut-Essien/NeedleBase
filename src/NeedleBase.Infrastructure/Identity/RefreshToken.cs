namespace NeedleBase.Infrastructure.Identity;

public class RefreshToken
{
    public string Id { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string? RevokedByIp { get; set; }
    public string? ReplacedByToken { get; set; }

    public string ApplicationUserId { get; set; } = string.Empty;
    public ApplicationUser? ApplicationUser { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresUtc;
    public bool IsActive => RevokedUtc is null && !IsExpired;
}
