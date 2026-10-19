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

    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is MeasurementEditViewModel vm && !vm.CanGoBack)
        {
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
