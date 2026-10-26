using MauiIcons.Material;
using NeedleBase.Application.Staff;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.Views;

/// <summary>More tab: Staff (Owner/Manager), your account (change password) and About.</summary>
public sealed class MorePage : ContentPage
{
    private readonly IAuthService _auth;
    private readonly ContentView _staffSlot = new();
    private string? _role;

    public MorePage(IAuthService auth)
    {
        _auth = auth;
        var body = new VerticalStackLayout { Spacing = 8 };
        body.Add(Ui.Heading("More"));
        body.Add(_staffSlot);

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
        body.Add(Ui.Card(about));
        body.Add(Ui.Body($"NeedleBase version {AppInfo.Current.VersionString}"));
        Ui.Chrome(this, "More", body, backButton: false);
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
                        Ui.Card(Ui.LinkRow("Staff", MaterialIcons.Group, () => Shell.Current.GoToAsync(nameof(StaffPage)), "MoreStaff",
                            StaffRoles.CanManage(role) ? "Invite someone to help, change roles" : "See who's on the team"))
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
