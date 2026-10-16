using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
        BindingContext = new HomeViewModel();
    }
}
