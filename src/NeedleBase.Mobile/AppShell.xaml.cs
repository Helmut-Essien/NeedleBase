using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.Views;

namespace NeedleBase.Mobile;

public partial class AppShell : Shell
{
    private static readonly TimeSpan DoubleBackWindow = TimeSpan.FromSeconds(2);
    private DateTimeOffset _lastRootBackPressUtc = DateTimeOffset.MinValue;

    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(CustomerEditPage), typeof(CustomerEditPage));
        Routing.RegisterRoute(nameof(CustomerDetailPage), typeof(CustomerDetailPage));
        Routing.RegisterRoute(nameof(MeasurementEditPage), typeof(MeasurementEditPage));
        Routing.RegisterRoute(nameof(CreateOrderPage), typeof(CreateOrderPage));
        Routing.RegisterRoute(nameof(OrderDetailPage), typeof(OrderDetailPage));
        Routing.RegisterRoute(nameof(TermsPage), typeof(TermsPage));
        Routing.RegisterRoute(nameof(PrivacyPage), typeof(PrivacyPage));

        // A stray first press must not survive a navigation and exit later.
        Navigated += (_, _) => _lastRootBackPressUtc = DateTimeOffset.MinValue;

        // Native MauiSplashScreen is the only splash; run auth gate once Shell is ready.
        Loaded += OnShellLoaded;
    }

    private async void OnShellLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnShellLoaded;
        var startup = ServiceHelper.GetRequiredService<AppStartupService>();
        await startup.RunOnceAsync(this);
    }

    /// <summary>
    /// Central Android Back handling (Shell is the root page for every screen):
    /// - a page/modal is on the stack -> pop it normally, no toast, no exit;
    /// - truly at a root (a tab root, or Login/Register root) -> first press shows
    ///   "Press back again to exit", a second press within 2s quits the app.
    /// Choice: every tab root counts as root (Back does not first jump to Home).
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (CanGoBack())
        {
            _lastRootBackPressUtc = DateTimeOffset.MinValue;
            _ = PopBackAsync();
            return true; // handled: we popped ourselves
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastRootBackPressUtc <= DoubleBackWindow)
        {
            _lastRootBackPressUtc = DateTimeOffset.MinValue;
            Microsoft.Maui.Controls.Application.Current?.Quit();
            return true;
        }

        _lastRootBackPressUtc = now;
        _ = ShowExitHintAsync();
        return true; // handled: do not exit on the first press
    }

    private bool CanGoBack()
    {
        try
        {
            if (Navigation.ModalStack.Count > 0 || Navigation.NavigationStack.Count > 1)
            {
                return true;
            }

            var current = CurrentPage;
            if (current is not null && !ReferenceEquals(current, this))
            {
                if (current.Navigation.ModalStack.Count > 0 ||
                    current.Navigation.NavigationStack.Count > 1)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Back check failed: {ex.Message}");
        }

        return false;
    }

    private async Task PopBackAsync()
    {
        try
        {
            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync();
            }
            else if (Navigation.NavigationStack.Count > 1)
            {
                await Navigation.PopAsync();
            }
            else if (CurrentPage is { } current && current.Navigation.NavigationStack.Count > 1)
            {
                await current.Navigation.PopAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Back pop failed: {ex.Message}");
        }
    }

    private static async Task ShowExitHintAsync()
    {
        try
        {
            // CommunityToolkit.Maui is already referenced and initialised
            // (UseMauiCommunityToolkit in MauiProgram); its Toast is the
            // native Android toast, not a custom-drawn substitute.
            await Toast.Make("Press back again to exit", ToastDuration.Short, 14).Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toast failed: {ex.Message}");
        }
    }
}
