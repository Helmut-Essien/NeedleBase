using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Auth;
using NeedleBase.Application.Customers;
using NeedleBase.Application.Orders;
using NeedleBase.Infrastructure.Health;
using NeedleBase.Infrastructure.Identity;
using NeedleBase.Infrastructure.Persistence;
using NeedleBase.Infrastructure.Time;

namespace NeedleBase.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton<LoginThrottle>();
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IMeasurementService, MeasurementService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ISyncService, NeedleBase.Application.Sync.SyncService>();
        services.AddHostedService<Sync.TombstoneCleanupService>();
        services.AddHostedService<CustomerKeyBackfillService>();
        services.AddSingleton<DatabaseHealthCheck>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. " +
                "Set it via user secrets or environment (ConnectionStrings__DefaultConnection).");
        }

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            }));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.User.RequireUniqueEmail = true;
                // No account-wide lockout: anyone could lock an owner out with their email. LoginThrottle slows
                // guessing per email + IP instead.
                options.Lockout.AllowedForNewUsers = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        var jwt = configuration.GetSection("JwtSettings");
        var key = jwt["SecurityKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            throw new InvalidOperationException(
                "JwtSettings:SecurityKey must be configured (min 32 characters) via user secrets or environment.");
        }

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                // Keep short claim names (role, business_id) — do not map to long ClaimTypes URIs.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt["Issuer"],
                    ValidAudience = jwt["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ClockSkew = TimeSpan.FromMinutes(5),
                    NameClaimType = "sub",
                    RoleClaimType = ClaimNames.Role
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var userManager = context.HttpContext.RequestServices
                            .GetRequiredService<UserManager<ApplicationUser>>();
                        var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                            ?? context.Principal?.FindFirst("sub")?.Value;
                        if (string.IsNullOrEmpty(userId))
                        {
                            context.Fail("Missing subject.");
                            return;
                        }

                        var user = await userManager.FindByIdAsync(userId);
                        if (user is null || !user.IsActive)
                        {
                            context.Fail("User inactive or missing.");
                            return;
                        }

                        var stamp = context.Principal?.FindFirst("security_stamp")?.Value;
                        if (!string.IsNullOrEmpty(user.SecurityStamp) &&
                            !string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal))
                        {
                            context.Fail("Security stamp mismatch.");
                            return;
                        }

                        var businessId = context.Principal?.FindFirst(ClaimNames.BusinessId)?.Value;
                        if (string.IsNullOrEmpty(businessId))
                        {
                            context.Fail("Missing business membership.");
                            return;
                        }

                        var db = context.HttpContext.RequestServices
                            .GetRequiredService<ApplicationDbContext>();
                        db.CurrentBusinessId = null;
                        var hasMembership = await db.StaffMemberships
                            .IgnoreQueryFilters()
                            .AsNoTracking()
                            .AnyAsync(
                                m => m.UserId == userId && m.BusinessId == businessId && m.IsActive,
                                context.HttpContext.RequestAborted);
                        if (!hasMembership)
                        {
                            context.Fail("Business membership inactive.");
                        }
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("RequireStaff", p => p.RequireAuthenticatedUser()
                .RequireClaim("business_id")
                .RequireRole(BusinessRole.Owner, BusinessRole.Manager, BusinessRole.Tailor))
            .AddPolicy("RequireOwner", p => p.RequireAuthenticatedUser()
                .RequireClaim("business_id")
                .RequireRole(BusinessRole.Owner))
            .AddPolicy("RequireManagerOrOwner", p => p.RequireAuthenticatedUser()
                .RequireClaim("business_id")
                .RequireRole(BusinessRole.Owner, BusinessRole.Manager));

        return services;
    }
}
