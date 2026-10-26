using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Settings;

/// <summary>
/// Owner/Manager: automatic WhatsApp updates (on/off and which statuses) plus the message log. When the server has no
/// Meta credentials the page says so and only tap-to-send (order page) is available.
/// </summary>
public class WhatsAppModel : AppPageModel
{
    private readonly INeedleBaseApi _api;

    public WhatsAppModel(INeedleBaseApi api)
    {
        _api = api;
    }

    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;
    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public List<string> Statuses { get; set; } = [];

    public WhatsAppSettingsDto? Settings { get; private set; }
    public WhatsAppMessagePageDto? Log { get; private set; }
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
        if (!User.CanSeeActivity())
        {
            return await LoadAsync(cancellationToken);
        }

        var result = await _api.UpdateWhatsAppSettingsAsync(
            new UpdateWhatsAppSettingsRequest { Enabled = Enabled, Statuses = Statuses.Take(16).ToList() }, cancellationToken);
        if (result.IsSuccess)
        {
            Flash = Enabled ? "Automatic WhatsApp updates are on." : "Automatic WhatsApp updates are off.";
            return RedirectToPage();
        }

        if (result.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            return Fail(result)!;
        }

        ActionError = result.Message;
        return await LoadAsync(cancellationToken);
    }

    private async Task<IActionResult> LoadAsync(CancellationToken cancellationToken)
    {
        if (!User.CanSeeActivity())
        {
            PageError = "WhatsApp settings are only shown to the owner and managers.";
            CanRetry = false;
            return Page();
        }

        var settingsTask = _api.GetWhatsAppSettingsAsync(cancellationToken);
        var logTask = _api.ListWhatsAppMessagesAsync(null, Math.Clamp(PageNumber, 1, 10_000), cancellationToken);
        await Task.WhenAll(settingsTask, logTask);
        var settings = await settingsTask;
        if (!settings.IsSuccess)
        {
            return Fail(settings) ?? Page();
        }

        Settings = settings.Value;
        if ((await logTask) is { IsSuccess: true } log)
        {
            Log = log.Value;
        }

        Loaded = true;
        return Page();
    }
}
