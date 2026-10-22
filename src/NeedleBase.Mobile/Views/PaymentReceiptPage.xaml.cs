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
}
