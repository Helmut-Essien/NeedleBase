using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NeedleBase.Api.Tests;

public class NeedleBaseApiFactory : WebApplicationFactory<Program>
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
            TestDatabase.ConnectionString);

        // Schema first, once for the whole run: the host's own Development migrate then finds nothing to do, and no
        // request (or background service) can reach an empty database.
        TestDatabase.EnsureMigrated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("JwtSettings:SecurityKey", TestJwtKey);
        builder.UseSetting("RateLimits:AuthPermitPerMinute", "100000");
        builder.UseSetting("RateLimits:RegisterPermitPer10Minutes", "100000");
        // Most tests register shops without a license key; LicensingApiTests turns this on with a fake Platform.
        builder.UseSetting("Platform:RequireLicenseKey", "false");
        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            TestDatabase.ConnectionString);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    TestDatabase.ConnectionString,
                ["JwtSettings:SecurityKey"] = TestJwtKey,
                ["JwtSettings:Issuer"] = "NeedleBaseApi",
                ["JwtSettings:Audience"] = "NeedleBaseClient",
                ["JwtSettings:AccessTokenExpirationMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpirationDays"] = "7",
                ["Seed:DevelopmentData"] = "false",
                ["RateLimits:AuthPermitPerMinute"] = "100000",
                ["RateLimits:RegisterPermitPer10Minutes"] = "100000",
                ["Platform:RequireLicenseKey"] = "false"
            });
        });
    }
}
