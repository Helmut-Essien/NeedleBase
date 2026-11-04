using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.Services.Updates;

namespace NeedleBase.Mobile.Views;

/// <summary>
/// Update prompt (modal). Optional: "Update now" or "Later" (no automatic prompt for that version again).
/// Required (this build is below minSupportedVersionCode): no "Later", Back does nothing, but sync keeps running
/// underneath and the update never deletes local data.
/// </summary>
public sealed class UpdatePage : ContentPage
{
    private readonly AppUpdateService _updates;
    private readonly UpdateManifest _manifest;
    private readonly bool _required;
    private readonly ProgressBar _progress = new() { IsVisible = false, ProgressColor = Ui.C("PrimaryDark", Colors.DarkGreen) };
    private readonly Label _status = Ui.Body();
    private readonly Label _error = new() { FontSize = 14, TextColor = Ui.C("SemanticOverdue", Colors.DarkRed), IsVisible = false };
    private readonly Button _update;
    private readonly Button _later;
    private CancellationTokenSource? _cts;

    public UpdatePage(AppUpdateService updates, UpdateCheckResult result)
    {
        _updates = updates;
        _manifest = result.Manifest!;
        _required = result.Kind == UpdateKind.Required;

        var body = new VerticalStackLayout { Spacing = 10 };
        body.Add(new Image
        {
            Source = "needlebase_logo.png",
            HeightRequest = 96,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 16, 0, 0),
            Aspect = Aspect.AspectFit
        });
        body.Add(Ui.Heading(_required ? "Update required" : "Update available"));
        body.Add(Ui.Body(_required
            ? $"NeedleBase {_manifest.VersionName} is needed to keep working with your shop's data. Your saved work stays on this phone and keeps syncing."
            : $"NeedleBase {_manifest.VersionName} is ready ({FormatSize(_manifest.Size)}).", muted: false));

        if (!string.IsNullOrWhiteSpace(_manifest.ReleaseNotes))
        {
            body.Add(Ui.Heading("What's new", 2));
            body.Add(Ui.Card(Ui.Body(_manifest.ReleaseNotes, muted: false)));
        }

        body.Add(_progress);
        body.Add(_status);
        body.Add(_error);

        _update = new Button
        {
            Text = "Update now",
            BackgroundColor = Ui.C("PrimaryDark", Colors.DarkGreen),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 16,
            CornerRadius = 10,
            HeightRequest = 48,
            Margin = new Thickness(0, 12, 0, 0),
            AutomationId = "UpdateNow"
        };
        _update.Clicked += async (_, _) => await UpdateAsync();
        body.Add(_update);

        _later = Ui.Secondary("Later");
        _later.AutomationId = "UpdateLater";
        _later.IsVisible = !_required;
        _later.Clicked += async (_, _) =>
        {
            _cts?.Cancel();
            _updates.Skip(_manifest);
            await CloseAsync();
        };
        body.Add(_later);

        Ui.Chrome(this, _required ? "Update required" : "Update available", body, backButton: false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (_required && ServiceHelper.GetService<SyncService>() is { } sync && await sync.PendingCountAsync() is var pending and > 0)
            {
                _status.Text = $"{pending} change(s) are waiting to sync. They are kept and sync before or after the update.";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update: pending count failed: {ex.Message}");
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (_required)
        {
            return true; // stays until updated
        }

        _cts?.Cancel();
        return base.OnBackButtonPressed();
    }

    private async Task UpdateAsync()
    {
        _update.IsEnabled = false;
        _error.IsVisible = false;
        _progress.IsVisible = true;
        _progress.Progress = 0;
        _status.Text = "Downloading…";
        _cts = new CancellationTokenSource();
        var progress = new Progress<double>(p =>
        {
            _progress.Progress = p;
            _status.Text = p >= 1 ? "Checking the download…" : $"Downloading… {p:P0}";
        });
        try
        {
            var outcome = await _updates.DownloadAndInstallAsync(_manifest, progress, _cts.Token);
            if (outcome == InstallOutcome.PermissionNeeded)
            {
                _progress.IsVisible = false;
                _status.Text = "Allow NeedleBase to install apps (turn on \"Allow from this source\"), come back, then tap Update now again.";
            }
            else
            {
                _status.Text = "Confirm the update on the next screen.";
                if (!_required)
                {
                    await CloseAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _status.Text = string.Empty;
        }
        catch (UpdateException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update failed: {ex}");
            ShowError("The update couldn't be installed. Please try again later.");
        }
        finally
        {
            _update.IsEnabled = true;
            _update.Text = "Update now";
        }
    }

    private void ShowError(string message)
    {
        _progress.IsVisible = false;
        _status.Text = string.Empty;
        _error.Text = message;
        _error.IsVisible = true;
        try { SemanticScreenReader.Default.Announce("Error. " + message); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Announce failed: {ex.Message}"); }
    }

    private async Task CloseAsync()
    {
        if (Navigation.ModalStack.Contains(this))
        {
            await Navigation.PopModalAsync();
        }
    }

    internal static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024d / 1024d:0.#} MB"
        : $"{Math.Max(1, bytes / 1024)} KB";
}
