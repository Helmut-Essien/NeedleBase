using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;

namespace NeedleBase.Infrastructure.Identity;

public sealed class TokenService : ITokenService
{
    private readonly SymmetricSecurityKey _key;
    private readonly SigningCredentials _creds;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenExpirationMinutes;
    private readonly int _refreshTokenExpirationDays;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public TokenService(IConfiguration configuration)
    {
        var jwt = configuration.GetSection("JwtSettings");
        var securityKey = jwt["SecurityKey"];
        if (string.IsNullOrWhiteSpace(securityKey) || securityKey.Length < 32)
        {
            throw new InvalidOperationException(
                "JwtSettings:SecurityKey must be configured (min 32 characters) via user secrets or environment.");
        }

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(securityKey));
        _creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        _issuer = jwt["Issuer"] ?? "NeedleBaseApi";
        _audience = jwt["Audience"] ?? "NeedleBaseClient";
        _accessTokenExpirationMinutes = int.Parse(jwt["AccessTokenExpirationMinutes"] ?? "60");
        _refreshTokenExpirationDays = int.Parse(jwt["RefreshTokenExpirationDays"] ?? "7");
    }

    public string CreateAccessToken(
        string userId,
        string email,
        string userName,
        string securityStamp,
        bool isActive,
        string businessId,
        string role)
    {
        var expiration = DateTime.UtcNow.AddMinutes(_accessTokenExpirationMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.UniqueName, userName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Email, email),
            new(JwtRegisteredClaimNames.Email, email),
            new("is_active", isActive ? "true" : "false"),
            new(ClaimNames.SecurityStamp, securityStamp),
            new(ClaimNames.BusinessId, businessId),
            new(ClaimNames.Role, role),
            new(ClaimTypes.Role, role)
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: expiration,
            signingCredentials: _creds);

        return _tokenHandler.WriteToken(token);
    }

    public RefreshTokenInfo CreateRefreshToken(string? ipAddress)
    {
        var randomBytes = new byte[48];
        RandomNumberGenerator.Fill(randomBytes);
        return new RefreshTokenInfo(
            Convert.ToBase64String(randomBytes),
            DateTime.UtcNow.AddDays(_refreshTokenExpirationDays),
            DateTime.UtcNow,
            ipAddress);
    }
}
