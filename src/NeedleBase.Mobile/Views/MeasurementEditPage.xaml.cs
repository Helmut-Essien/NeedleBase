using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class MeasurementEditPage : ContentPage
{
    public MeasurementEditPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<MeasurementEditViewModel>();
    }
}
