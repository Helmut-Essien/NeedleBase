using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class RecordPaymentPage : ContentPage
{
    public RecordPaymentPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<RecordPaymentViewModel>();
    }
}
