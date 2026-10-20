using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NeedleBase.Api.Tests;

public sealed class NeedleBaseApiFactory : WebApplicationFactory<Program>
{
    private const string TestJwtKey = "NeedleBase-Test-SecurityKey-Min-32-Chars!!";

    public NeedleBaseApiFactory()
    {
        // Env vars override user secrets so TokenService and JwtBearer share one key.
        Environment.SetEnvironmentVariable(
            "JwtSettings__SecurityKey",
            TestJwtKey);
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5435;Database=NeedleBase;Username=needlebase;Password=needlebase_dev");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("JwtSettings:SecurityKey", TestJwtKey);
        builder.UseSetting("RateLimits:AuthPermitPerMinute", "100000");
        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5435;Database=NeedleBase;Username=needlebase;Password=needlebase_dev");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                    ?? "Host=localhost;Port=5435;Database=NeedleBase;Username=needlebase;Password=needlebase_dev",
                ["JwtSettings:SecurityKey"] = TestJwtKey,
                ["JwtSettings:Issuer"] = "NeedleBaseApi",
                ["JwtSettings:Audience"] = "NeedleBaseClient",
                ["JwtSettings:AccessTokenExpirationMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpirationDays"] = "7",
                ["Seed:DevelopmentData"] = "false",
                ["RateLimits:AuthPermitPerMinute"] = "100000"
            });
        });
    }
}
