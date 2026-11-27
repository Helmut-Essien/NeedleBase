using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class PaymentReceiptPage : ContentPage
{
    public PaymentReceiptPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<PaymentReceiptViewModel>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ((PaymentReceiptViewModel)BindingContext).Appear();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ((PaymentReceiptViewModel)BindingContext).Disappear();
    }
}
