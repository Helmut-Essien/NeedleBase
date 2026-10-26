using CommunityToolkit.Mvvm.Messaging;
using NeedleBase.Mobile.Messaging;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Services;

public interface INavigationService
{
    Task ShowLoginAsync();
    Task ShowMainAsync();
}

public sealed class NavigationService : INavigationService
{
    private static readonly string[] MainTabRoutes =
    [
        "//main/HomePage",
        "//main/CustomersPage",
        "//main/OrdersPage",
        "//main/PaymentsPage",
        "//main/MorePage"
    ];

    private readonly CustomersViewModel _customersViewModel;
    private readonly OrdersViewModel _ordersViewModel;

    private readonly PaymentsViewModel? _paymentsViewModel;

    public NavigationService(CustomersViewModel customersViewModel, OrdersViewModel ordersViewModel, PaymentsViewModel? paymentsViewModel = null)
    {
        _paymentsViewModel = paymentsViewModel;
        _customersViewModel = customersViewModel;
        _ordersViewModel = ordersViewModel;
    }

    public async Task ShowLoginAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        // Clear singleton tab VM PII on every path to login (logout + session expiry).
        _customersViewModel.ClearSessionState();
        _ordersViewModel.ClearSessionState();
        _paymentsViewModel?.ClearSessionState();
        // Drop cached Login/Register form secrets (Shell may reuse auth pages/VMs).
        WeakReferenceMessenger.Default.Send(new ClearAuthSensitiveStateMessage());

        await DismissOverlaysAndPopToRootAsync();
        await Shell.Current.GoToAsync("//LoginPage");
    }

    public async Task ShowMainAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        await Shell.Current.GoToAsync("//main/HomePage");
        // Slice 4: sync on app start / after login (push then pull; offline-safe).
        ServiceHelper.GetRequiredService<SyncService>().Start();
    }

    private static async Task DismissOverlaysAndPopToRootAsync()
    {
        var shell = Shell.Current;
        if (shell is null)
        {
            return;
        }

        try
        {
            while (shell.Navigation.ModalStack.Count > 0)
            {
                await shell.Navigation.PopModalAsync(animated: false);
            }

            foreach (var route in MainTabRoutes)
            {
                try
                {
                    await shell.GoToAsync(route, animate: false);
                    while (shell.Navigation.NavigationStack.Count > 1)
                    {
                        await shell.Navigation.PopAsync(animated: false);
                    }
                }
                catch (Exception ex)
                {
                    // e.g. the Payments tab is hidden for a Tailor: skip it, still clear the other tabs.
                    System.Diagnostics.Debug.WriteLine($"Stack clear skipped {route}: {ex.Message}");
                }
            }

            await shell.GoToAsync("//main/HomePage", animate: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShowLoginAsync stack clear failed: {ex.Message}");
        }
    }
}
