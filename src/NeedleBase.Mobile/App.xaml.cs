using NeedleBase.Mobile.ViewModels;
using NeedleBase.Mobile.Views;

namespace NeedleBase.Mobile;

public partial class App : Microsoft.Maui.Controls.Application
{
    public App()
    {
        InitializeComponent();
        // Light-only design: every page paints light surfaces (AppBackground/White).
        // Following a dark system theme would turn default text white on white.
        UserAppTheme = AppTheme.Light;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        window.Stopped += OnWindowStopped;
        window.Stopped += (_, _) => Sync?.OnBackground();
        window.Activated += (_, _) => Sync?.OnForeground(); // first show and every return to the foreground
        return window;
    }

    private static NeedleBase.Mobile.Services.SyncService? Sync =>
        NeedleBase.Mobile.Services.ServiceHelper.GetService<NeedleBase.Mobile.Services.SyncService>();

    private static void OnWindowStopped(object? sender, EventArgs e)
    {
        // Remask only — do not clear form text (user may resume mid-entry).
        switch (Shell.Current?.CurrentPage)
        {
            case LoginPage { BindingContext: LoginViewModel loginVm }:
                loginVm.RemaskPassword();
                break;
            case RegisterPage { BindingContext: RegisterViewModel registerVm }:
                registerVm.RemaskPasswords();
                break;
        }
    }
}
