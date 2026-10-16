using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class CustomerDetailPage : ContentPage
{
    public CustomerDetailPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<CustomerDetailViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Reload when returning from edit/measurement so detail is not stale.
        await ((CustomerDetailViewModel)BindingContext).LoadAsync();
    }
}
