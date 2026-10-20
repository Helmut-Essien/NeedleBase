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

    public NavigationService(CustomersViewModel customersViewModel, OrdersViewModel ordersViewModel)
    {
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
                await shell.GoToAsync(route, animate: false);
                while (shell.Navigation.NavigationStack.Count > 1)
                {
                    await shell.Navigation.PopAsync(animated: false);
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
