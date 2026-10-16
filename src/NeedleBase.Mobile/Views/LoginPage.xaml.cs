using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<LoginViewModel>();
    }

    private async void OnRegisterClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//RegisterPage");
    }
}
