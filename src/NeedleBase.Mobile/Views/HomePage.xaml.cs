using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

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
        await auth.LogoutAsync();
        await navigation.ShowLoginAsync();
    }
}
