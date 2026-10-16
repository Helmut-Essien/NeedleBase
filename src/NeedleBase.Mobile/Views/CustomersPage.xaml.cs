using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class CustomersPage : ContentPage
{
    public CustomersPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<CustomersViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ((CustomersViewModel)BindingContext).LoadCommand.ExecuteAsync(null);
    }
}
