using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class OrdersPage : ContentPage
{
    public OrdersPage()
    {
        var perf = System.Diagnostics.Stopwatch.StartNew();
        InitializeComponent();
        Perf.Log("orders.pageCtor", perf.ElapsedMilliseconds);
        BindingContext = ServiceHelper.GetRequiredService<OrdersViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var perf = System.Diagnostics.Stopwatch.StartNew();
        Perf.Log("orders.appearing", 0);
        await ((OrdersViewModel)BindingContext).AppearAsync();
        Perf.Log("orders.loadDone", perf.ElapsedMilliseconds);
        Dispatcher.Dispatch(() => Perf.Log("orders.firstLayoutAfterLoad", perf.ElapsedMilliseconds));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ((OrdersViewModel)BindingContext).Disappear();
    }
}
