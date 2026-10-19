using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class RegisterPage : ContentPage
{
    public RegisterPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<RegisterViewModel>();
    }

    private async void OnTermsClicked(object? sender, EventArgs e)
    {
        if (BindingContext is RegisterViewModel vm && !vm.CanSubmit)
        {
            return;
        }

        await Shell.Current.GoToAsync(nameof(TermsPage));
    }

    private async void OnPrivacyClicked(object? sender, EventArgs e)
    {
        if (BindingContext is RegisterViewModel vm && !vm.CanSubmit)
        {
            return;
        }

        await Shell.Current.GoToAsync(nameof(PrivacyPage));
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        if (BindingContext is RegisterViewModel vm && !vm.CanSubmit)
        {
            return;
        }

        await Shell.Current.GoToAsync("//LoginPage");
    }
}
