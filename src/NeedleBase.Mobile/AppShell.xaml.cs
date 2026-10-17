using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.Views;

namespace NeedleBase.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(CustomerEditPage), typeof(CustomerEditPage));
        Routing.RegisterRoute(nameof(CustomerDetailPage), typeof(CustomerDetailPage));
        Routing.RegisterRoute(nameof(MeasurementEditPage), typeof(MeasurementEditPage));

        // Native MauiSplashScreen is the only splash; run auth gate once Shell is ready.
        Loaded += OnShellLoaded;
    }

    private async void OnShellLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnShellLoaded;
        var startup = ServiceHelper.GetRequiredService<AppStartupService>();
        await startup.RunOnceAsync(this);
    }
}
