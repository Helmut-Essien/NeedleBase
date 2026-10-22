using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class OrderDetailPage : ContentPage
{
    public OrderDetailPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<OrderDetailViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ((OrderDetailViewModel)BindingContext).AppearAsync();
    }
}
