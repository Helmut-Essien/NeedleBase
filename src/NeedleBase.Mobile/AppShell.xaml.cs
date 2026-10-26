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
        Routing.RegisterRoute(nameof(WhatsAppSettingsPage), typeof(WhatsAppSettingsPage));
        Routing.RegisterRoute(nameof(MeasurementEditPage), typeof(MeasurementEditPage));
        Routing.RegisterRoute(nameof(CreateOrderPage), typeof(CreateOrderPage));
        Routing.RegisterRoute(nameof(OrderDetailPage), typeof(OrderDetailPage));
        Routing.RegisterRoute(nameof(EditOrderPage), typeof(EditOrderPage));
        Routing.RegisterRoute(nameof(SyncDetailsPage), typeof(SyncDetailsPage));
        Routing.RegisterRoute(nameof(RecordPaymentPage), typeof(RecordPaymentPage));
        Routing.RegisterRoute(nameof(PaymentReceiptPage), typeof(PaymentReceiptPage));
        Routing.RegisterRoute(nameof(TermsPage), typeof(TermsPage));
        Routing.RegisterRoute(nameof(PrivacyPage), typeof(PrivacyPage));
        Routing.RegisterRoute(nameof(StaffPage), typeof(StaffPage));
        Routing.RegisterRoute(nameof(ChangePasswordPage), typeof(ChangePasswordPage));

        // A stray first press must not survive a navigation and exit later.
        Navigated += (_, _) =>
        {
            _lastRootBackPressUtc = DateTimeOffset.MinValue;
            _ = ApplyRoleTabsAsync();
        };

        // Native MauiSplashScreen is the only splash; run auth gate once Shell is ready.
        Loaded += OnShellLoaded;
    }

    /// <summary>Tailors never see payments: hide the tab (role from the cached session; refreshed on every navigation).</summary>
    private async Task ApplyRoleTabsAsync()
    {
        try
        {
            var role = (await ServiceHelper.GetRequiredService<IAuthService>().GetCachedUserAsync())?.Role;
            var show = role is null || NeedleBase.Application.Payments.PaymentRules.CanManage(role);
            if (PaymentsTab.IsVisible != show)
            {
                PaymentsTab.IsVisible = show;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Role tabs failed: {ex.Message}");
        }
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
        // The page's view model first: no pop while it is saving (the save then navigates itself, which would
        // pop a second page), and a wizard steps back before leaving.
        if (CurrentPage?.BindingContext is IBackAware aware)
        {
            if (!aware.CanGoBack || aware.TryHandleBack())
            {
                return true;
            }
        }

        if (_popping)
        {
            return true; // a pop is already running: a fast second press must not pop the page below too
        }

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

    private bool _popping;

    private async Task PopBackAsync()
    {
        _popping = true;
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
        finally
        {
            _popping = false;
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
