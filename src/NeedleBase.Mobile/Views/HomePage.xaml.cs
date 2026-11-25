using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

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
        await ApplyFreeChromeAsync();
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

    private async Task ApplyFreeChromeAsync()
    {
        try
        {
            var free = await ServiceHelper.GetRequiredService<IAuthService>().IsFreeModeAsync();
            FreeBanner.IsVisible = free;
            SyncChip.IsVisible = !free;
            SignOutButton.Text = free ? "Leave Free on this phone" : "Sign out";
            SemanticProperties.SetDescription(
                SignOutButton,
                free
                    ? "Leave Free on this phone and clear records on this device"
                    : "Sign out of this business");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Home Free chrome failed: {ex.Message}");
        }
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        var auth = ServiceHelper.GetRequiredService<IAuthService>();
        var navigation = ServiceHelper.GetRequiredService<INavigationService>();
        var sync = ServiceHelper.GetRequiredService<SyncService>();
        var isFree = await auth.IsFreeModeAsync();

        if (isFree)
        {
            if (!await DisplayAlert(
                    "Leave Free on this phone?",
                    "This removes Free records from this phone. NeedleBase does not keep a cloud copy. Save a copy from More first if you need one.",
                    "Leave and clear",
                    "Stay"))
            {
                return;
            }
        }
        else
        {
            var unsynced = await sync.PendingCountAsync();
            if (unsynced > 0 && !await DisplayAlert(
                    "Changes not synced",
                    $"{unsynced} change(s) on this device haven't synced yet. Signing out deletes them from this device.",
                    "Sign out anyway",
                    "Stay signed in"))
            {
                return;
            }
        }

        // Stop and wait for any sync in flight first, so nothing is written after the wipe.
        await sync.StopAsync();
        var wiped = await auth.LogoutAsync();
        if (!wiped)
        {
            if (!isFree)
            {
                sync.Start(); // still signed in: carry on syncing
            }

            await DisplayAlert(
                isFree ? "Couldn't leave Free" : "Couldn't sign out",
                "Local data couldn't be cleared. Stay on this phone and try again.",
                "OK");
            return;
        }

        // ShowLoginAsync clears singleton tab VM state (and pops stacks).
        await navigation.ShowLoginAsync();
    }
}
