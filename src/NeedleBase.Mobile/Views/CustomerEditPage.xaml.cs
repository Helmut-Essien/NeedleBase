using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class CustomerEditPage : ContentPage
{
    public CustomerEditPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<CustomerEditViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Add-only fallback when Shell skips ApplyQueryAttributes (no query).
        // Yields first so edit query attrs can latch InitializeAsync ahead of this.
        await ((CustomerEditViewModel)BindingContext).InitializeIfPendingAsync();
    }
}
