using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class PaymentsPage : ContentPage
{
    public PaymentsPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<PaymentsViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ((PaymentsViewModel)BindingContext).AppearAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ((PaymentsViewModel)BindingContext).Disappear();
    }
}
