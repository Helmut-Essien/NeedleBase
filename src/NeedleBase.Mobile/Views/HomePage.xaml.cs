using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels; // HomeViewModel

namespace NeedleBase.Mobile.Views;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<HomeViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is HomeViewModel vm)
        {
            await vm.LoadCommand.ExecuteAsync(null);
        }
    }

    private async void OnBellClicked(object? sender, EventArgs e)
    {
        // No notifications system yet — say so honestly instead of faking a feed.
        await DisplayAlert("Notifications", "No new notifications.", "OK");
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        var auth = ServiceHelper.GetRequiredService<IAuthService>();
        var navigation = ServiceHelper.GetRequiredService<INavigationService>();
        var wiped = await auth.LogoutAsync();
        if (!wiped)
        {
            await DisplayAlert(
                "Couldn't sign out",
                "Local data couldn't be cleared. Stay signed in and try again.",
                "OK");
            return;
        }

        // ShowLoginAsync clears singleton tab VM state (and pops stacks).
        await navigation.ShowLoginAsync();
    }
}
