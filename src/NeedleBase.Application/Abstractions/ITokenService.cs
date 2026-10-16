namespace NeedleBase.Application.Abstractions;

public interface ITokenService
{
    string CreateAccessToken(
        string userId,
        string email,
        string userName,
        string securityStamp,
        bool isActive,
        string businessId,
        string role);

    RefreshTokenInfo CreateRefreshToken(string? ipAddress);
}

public sealed record RefreshTokenInfo(
    string Token,
    DateTime ExpiresUtc,
    DateTime CreatedUtc,
    string? CreatedByIp);
