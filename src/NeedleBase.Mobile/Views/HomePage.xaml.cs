using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels; // HomeViewModel

namespace NeedleBase.Mobile.Views;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<HomeViewModel>();
        SyncChip.BindingContext = ServiceHelper.GetRequiredService<SyncStatusViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is HomeViewModel vm)
        {
            await vm.AppearAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        (BindingContext as HomeViewModel)?.Disappear();
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        var auth = ServiceHelper.GetRequiredService<IAuthService>();
        var navigation = ServiceHelper.GetRequiredService<INavigationService>();
        var sync = ServiceHelper.GetRequiredService<SyncService>();
        var unsynced = await sync.PendingCountAsync();
        if (unsynced > 0 && !await DisplayAlert(
                "Changes not synced",
                $"{unsynced} change(s) on this device haven't synced yet. Signing out deletes them from this device.",
                "Sign out anyway",
                "Stay signed in"))
        {
            return;
        }

        // Stop and wait for any sync in flight first, so nothing is written after the wipe.
        await sync.StopAsync();
        var wiped = await auth.LogoutAsync();
        if (!wiped)
        {
            sync.Start(); // still signed in: carry on syncing
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
