using MauiIcons.Material;
using NeedleBase.Application.Staff;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.Services.Updates;

namespace NeedleBase.Mobile.Views;

/// <summary>More tab: Staff (Owner/Manager), your account (change password) and About.</summary>
public sealed class MorePage : ContentPage
{
    private readonly IAuthService _auth;
    private readonly ContentView _staffSlot = new();
    private string? _role;

    public MorePage(IAuthService auth, AppUpdateService updates)
    {
        _auth = auth;
        var body = new VerticalStackLayout { Spacing = 8 };
        body.Add(Ui.Heading("More"));
        body.Add(_staffSlot);

        body.Add(Ui.Heading("This device", 2));
        body.Add(Ui.Card(Ui.LinkRow("Sync details", MaterialIcons.Sync, () => Shell.Current.GoToAsync(nameof(SyncDetailsPage)), "MoreSyncDetails",
            "What's waiting to sync, problems and conflicts")));

        body.Add(Ui.Heading("Your account", 2));
        body.Add(Ui.Card(Ui.LinkRow("Change password", MaterialIcons.Lock, () => Shell.Current.GoToAsync(nameof(ChangePasswordPage)), "MoreChangePassword")));

        body.Add(Ui.Heading("About", 2));
        var about = new VerticalStackLayout
        {
            Children =
            {
                Ui.LinkRow("Terms and Conditions", MaterialIcons.Description, () => Shell.Current.GoToAsync(nameof(TermsPage)), "MoreTermsLink"),
                Ui.Divider(),
                Ui.LinkRow("Privacy Policy", MaterialIcons.PrivacyTip, () => Shell.Current.GoToAsync(nameof(PrivacyPage)), "MorePrivacyLink")
            }
        };
        if (updates.IsEnabled)
        {
            about.Add(Ui.Divider());
            about.Add(Ui.LinkRow("Check for updates", MaterialIcons.SystemUpdate, () => CheckForUpdatesAsync(updates), "MoreCheckUpdates",
                "Download the newest NeedleBase"));
        }

        body.Add(Ui.Card(about));
        body.Add(Ui.Body($"NeedleBase version {AppInfo.Current.VersionString}"));
        Ui.Chrome(this, "More", body, backButton: false);
    }

    private bool _checking;

    private async Task CheckForUpdatesAsync(AppUpdateService updates)
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        try
        {
            var result = await updates.CheckAsync(manual: true);
            if (result.Kind != UpdateKind.None && result.Manifest is not null)
            {
                await UpdatePrompter.ShowAsync(updates, result);
            }
            else
            {
                await DisplayAlert("Check for updates", result.Error ?? $"You have the newest NeedleBase ({AppInfo.Current.VersionString}).", "OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"More: update check failed: {ex.Message}");
            await DisplayAlert("Check for updates", "Couldn't check for updates. Please try again later.", "OK");
        }
        finally
        {
            _checking = false;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var role = (await _auth.GetCachedUserAsync())?.Role;
            if (role == _role && _staffSlot.Content is not null)
            {
                return;
            }

            _role = role;
            _staffSlot.Content = StaffRoles.CanView(role)
                ? new VerticalStackLayout
                {
                    Children =
                    {
                        Ui.Heading("Team", 2),
                        Ui.Card(new VerticalStackLayout
                        {
                            Children =
                            {
                                Ui.LinkRow("Staff", MaterialIcons.Group, () => Shell.Current.GoToAsync(nameof(StaffPage)), "MoreStaff",
                                    StaffRoles.CanManage(role) ? "Invite someone to help, change roles" : "See who's on the team"),
                                Ui.Divider(),
                                Ui.LinkRow("WhatsApp", MaterialIcons.Chat, () => Shell.Current.GoToAsync(nameof(WhatsAppSettingsPage)), "MoreWhatsApp",
                                    "Automatic order updates and message log")
                            }
                        })
                    }
                }
                : new ContentView();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"More: role read failed: {ex.Message}");
        }
    }
}
