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
