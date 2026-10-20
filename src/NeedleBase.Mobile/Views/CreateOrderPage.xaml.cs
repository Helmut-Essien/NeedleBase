using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class CreateOrderPage : ContentPage
{
    public CreateOrderPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<CreateOrderViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Yields so ApplyQueryAttributes can set PrefillCustomerId before init latches.
        if (BindingContext is CreateOrderViewModel vm)
        {
            await vm.InitializeIfPendingAsync();
        }
    }
}
