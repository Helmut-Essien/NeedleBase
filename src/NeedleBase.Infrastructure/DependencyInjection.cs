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
        services.AddScoped<IStaffService, NeedleBase.Infrastructure.Staff.StaffService>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<ResetRequestIpCap>();
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IMeasurementService, MeasurementService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<NeedleBase.Application.Dashboard.IDashboardService, NeedleBase.Application.Dashboard.DashboardService>();
        services.AddScoped<NeedleBase.Application.Payments.IPaymentService, NeedleBase.Application.Payments.PaymentService>();
        services.AddScoped<ISyncService, NeedleBase.Application.Sync.SyncService>();
        services.AddHostedService<Sync.TombstoneCleanupService>();
        // Audit trail: request context (scoped), security/activity writer, read side, throttle, retention purge.
        services.AddScoped<NeedleBase.Application.Audit.AuditContext>();
        services.AddScoped<NeedleBase.Application.Audit.IAuditLog, Audit.AuditLog>();
        services.AddScoped<NeedleBase.Application.Audit.IAuditQueryService, Audit.AuditQueryService>();
        services.AddSingleton<Audit.AuditThrottle>();
        services.AddHostedService<Audit.AuditRetentionService>();
        services.AddHostedService<CustomerKeyBackfillService>();
        services.AddSingleton<DatabaseHealthCheck>();
        // Email (SMTP, e.g. Gmail + App Password). Off when unset: codes are shared by hand, Forgot password hidden.
        services.Configure<Email.EmailOptions>(configuration.GetSection(Email.EmailOptions.Section));
        services.PostConfigure<Email.EmailOptions>(o =>
        {
            if (string.IsNullOrWhiteSpace(o.AppBaseUrl))
            {
                o.AppBaseUrl = configuration["FrontendBaseUrl"];
            }
        });
        services.AddSingleton<Email.EmailQueue>();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<Email.EmailQueue>());
        services.AddSingleton<Email.IEmailTransport, Email.SmtpTransport>();
        services.AddHostedService<Email.EmailSendingService>();
        // WhatsApp (Meta Cloud API). Off when unset; secrets only from user-secrets/environment.
        services.Configure<WhatsApp.WhatsAppOptions>(configuration.GetSection(WhatsApp.WhatsAppOptions.Section));
        services.AddHttpClient<WhatsApp.IWhatsAppClient, WhatsApp.MetaWhatsAppClient>();
        services.AddScoped<WhatsApp.WhatsAppService>();
        services.AddHostedService<WhatsApp.WhatsAppSender>();

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
                options.Password.RequiredLength = NeedleBase.Application.Common.PasswordPolicy.MinLength;
                options.Password.RequireLowercase = true; // PasswordPolicy: digit + small letter
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
                        var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                            ?? context.Principal?.FindFirst("sub")?.Value;
                        if (string.IsNullOrEmpty(userId))
                        {
                            context.Fail("Missing subject.");
                            return;
                        }

                        var businessId = context.Principal?.FindFirst(ClaimNames.BusinessId)?.Value;
                        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                        db.CurrentBusinessId = null;

                        // Every request re-checks the account (deactivated, signed out everywhere, removed from the
                        // business) against the database: one round trip instead of two (user row + membership).
                        var aborted = context.HttpContext.RequestAborted;
                        if (aborted.IsCancellationRequested)
                        {
                            context.NoResult(); // caller already gone (navigated away): nothing to check or log
                            return;
                        }

                        var check = await db.Users.AsNoTracking()
                            .Where(u => u.Id == userId)
                            .Select(u => new
                            {
                                u.IsActive,
                                u.SecurityStamp,
                                Member = businessId != null && db.StaffMemberships.IgnoreQueryFilters()
                                    .Any(m => m.UserId == userId && m.BusinessId == businessId && m.IsActive)
                            })
                            .FirstOrDefaultAsync(CancellationToken.None); // ~1 ms; a cancelled lookup only produced auth-failure noise
                        if (check is null || !check.IsActive)
                        {
                            context.Fail("User inactive or missing.");
                            return;
                        }

                        var stamp = context.Principal?.FindFirst("security_stamp")?.Value;
                        if (!string.IsNullOrEmpty(check.SecurityStamp) &&
                            !string.Equals(stamp, check.SecurityStamp, StringComparison.Ordinal))
                        {
                            context.Fail("Security stamp mismatch.");
                            return;
                        }

                        if (string.IsNullOrEmpty(businessId))
                        {
                            context.Fail("Missing business membership.");
                            return;
                        }

                        if (!check.Member)
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
