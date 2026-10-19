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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is RegisterViewModel vm)
        {
            vm.PrepareForDisplay();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is RegisterViewModel vm)
        {
            vm.RemaskPasswords();
        }
    }

    private async void OnTermsClicked(object? sender, EventArgs e)
    {
        if (BindingContext is not RegisterViewModel vm || !vm.CanSubmit)
        {
            return;
        }

        await Shell.Current.GoToAsync(nameof(TermsPage));
    }

    private async void OnPrivacyClicked(object? sender, EventArgs e)
    {
        if (BindingContext is not RegisterViewModel vm || !vm.CanSubmit)
        {
            return;
        }

        await Shell.Current.GoToAsync(nameof(PrivacyPage));
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        if (BindingContext is not RegisterViewModel vm || !vm.CanSubmit)
        {
            return;
        }

        vm.ClearSensitiveFields();
        await Shell.Current.GoToAsync("//LoginPage");
    }
}
