using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class EditOrderPage : ContentPage
{
    public EditOrderPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<EditOrderViewModel>();
    }
}
