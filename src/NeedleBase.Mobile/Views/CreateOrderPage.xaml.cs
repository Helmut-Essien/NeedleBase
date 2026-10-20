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

            // Autofocus the customer search when nothing is pre-selected.
            if (vm.ShowCustomerPicker && vm.IsStepCustomer)
            {
                await Task.Delay(150);
                CustomerSearchEntry.Focus();
            }
        }
    }
}
