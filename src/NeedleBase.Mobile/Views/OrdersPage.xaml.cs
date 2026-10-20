using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class OrdersPage : ContentPage
{
    public OrdersPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<OrdersViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ((OrdersViewModel)BindingContext).LoadCommand.ExecuteAsync(null);
    }
}
