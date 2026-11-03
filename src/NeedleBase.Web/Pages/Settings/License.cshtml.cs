using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Settings;

/// <summary>
/// Owner: the shop's NeedleBase license (plan, expiry, last check) and adding or replacing the key. Shops created before
/// licensing use this once to attach their key. The key itself is never shown — only its last 4 characters.
/// </summary>
public class LicenseModel : AppPageModel
{
    private readonly INeedleBaseApi _api;
    private readonly IMemoryCache _cache;

    public LicenseModel(INeedleBaseApi api, IMemoryCache cache)
    {
        _api = api;
        _cache = cache;
    }

    [BindProperty] public string? LicenseKey { get; set; }

    public SubscriptionStatusDto? Status { get; private set; }
    public string? StatusMessage { get; private set; }
    public string? ActionError { get; private set; }

    [TempData] public string? Flash { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        StatusMessage = Flash;
        return await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!User.IsOwner())
        {
            return await LoadAsync(cancellationToken);
        }

        var key = LicenseKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            ActionError = NeedleBase.Application.Licensing.LicenseErrors.MessageFor(NeedleBase.Application.Licensing.LicenseErrors.KeyRequired);
            return await LoadAsync(cancellationToken);
        }

        if (key.Length > 200)
        {
            ActionError = NeedleBase.Application.Licensing.LicenseErrors.MessageFor(NeedleBase.Application.Licensing.LicenseErrors.KeyInvalid);
            return await LoadAsync(cancellationToken);
        }

        var result = await _api.AttachLicenseAsync(key, cancellationToken);
        if (result.IsSuccess)
        {
            SubscriptionBannerViewComponent.Forget(_cache, User);
            Flash = "License key added.";
            return RedirectToPage();
        }

        if (result.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            return Fail(result)!;
        }

        LicenseKey = null; // never echo a key back into the page
        ActionError = result.Message;
        return await LoadAsync(cancellationToken);
    }

    private async Task<IActionResult> LoadAsync(CancellationToken cancellationToken)
    {
        if (!User.IsOwner())
        {
            PageError = "The license is only shown to the shop owner.";
            CanRetry = false;
            return Page();
        }

        var status = await _api.GetSubscriptionAsync(cancellationToken);
        if (!status.IsSuccess)
        {
            return Fail(status) ?? Page();
        }

        Status = status.Value;
        Loaded = true;
        return Page();
    }
}
