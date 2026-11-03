using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Licensing;
using NeedleBase.Infrastructure.Licensing;

namespace NeedleBase.Api.Http;

/// <summary>Controllers that keep working while a shop is read-only (sign-in, sync, license, profile, team).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowWhenReadOnlyAttribute : Attribute;

/// <summary>
/// Soft lock: when the shop's license is read-only, business writes (anything but GET/HEAD/OPTIONS) get
/// 403 SUBSCRIPTION_READ_ONLY. Reading, sync (so earlier offline changes still land), sign-in and attaching a
/// license always work. Status is cached 30 s per shop, so this costs a memory lookup per write.
/// </summary>
public sealed class SubscriptionWriteGuard : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method))
        {
            await next();
            return;
        }

        var metadata = context.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<AllowWhenReadOnlyAttribute>().Any() || metadata.OfType<IAllowAnonymous>().Any())
        {
            await next();
            return;
        }

        var businessId = http.RequestServices.GetRequiredService<ICurrentUser>().BusinessId;
        if (!string.IsNullOrEmpty(businessId))
        {
            var status = await http.RequestServices.GetRequiredService<LicenseService>().GetStatusAsync(businessId, http.RequestAborted);
            if (status.IsReadOnly)
            {
                context.Result = ApiProblems.Create(http, StatusCodes.Status403Forbidden, LicenseErrors.ReadOnly,
                    "New changes are paused", status.Message ?? LicenseErrors.ReadOnlyMessage);
                return;
            }
        }

        await next();
    }
}
