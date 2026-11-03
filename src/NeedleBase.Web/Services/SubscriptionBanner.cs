using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NeedleBase.Application.Contracts.Licensing;

namespace NeedleBase.Web.Services;

/// <summary>
/// License banner in the signed-in layout. The status is cached per shop for a minute so pages don't each call the
/// Api; when the Api can't be reached there is simply no banner (never a guess, never a lock in the browser — the Api
/// decides what's allowed).
/// </summary>
public sealed class SubscriptionBannerViewComponent : ViewComponent
{
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AfterFailure = TimeSpan.FromSeconds(20);

    private readonly INeedleBaseApi _api;
    private readonly IMemoryCache _cache;

    public SubscriptionBannerViewComponent(INeedleBaseApi api, IMemoryCache cache)
    {
        _api = api;
        _cache = cache;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var principal = UserClaimsPrincipal;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return Content(string.Empty);
        }

        var status = await GetAsync(_api, _cache, principal, HttpContext.RequestAborted);
        if (status?.Message is null || (status.OwnerOnly && !principal.IsOwner()))
        {
            return Content(string.Empty);
        }

        return View(status);
    }

    internal static string Key(ClaimsPrincipal user) =>
        "nb-sub:" + (user.FindFirst("BusinessId")?.Value ?? user.UserId() ?? "anon");

    internal static async Task<SubscriptionStatusDto?> GetAsync(INeedleBaseApi api, IMemoryCache cache, ClaimsPrincipal user, CancellationToken ct)
    {
        var key = Key(user);
        if (cache.TryGetValue(key, out SubscriptionStatusDto? cached))
        {
            return cached;
        }

        var result = await api.GetSubscriptionAsync(ct);
        var value = result.IsSuccess ? result.Value : null;
        cache.Set(key, value, value is null ? AfterFailure : Fresh);
        return value;
    }

    /// <summary>After the Owner attaches a key, show the new state on the next page.</summary>
    internal static void Forget(IMemoryCache cache, ClaimsPrincipal user) => cache.Remove(Key(user));
}
