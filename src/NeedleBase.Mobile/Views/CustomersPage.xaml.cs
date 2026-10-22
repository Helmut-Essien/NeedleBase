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
        var perf = System.Diagnostics.Stopwatch.StartNew();
        await ((CustomersViewModel)BindingContext).AppearAsync();
        NeedleBase.Mobile.Helpers.Perf.Log("customers.loadDone", perf.ElapsedMilliseconds);
        Dispatcher.Dispatch(() => NeedleBase.Mobile.Helpers.Perf.Log("customers.firstLayoutAfterLoad", perf.ElapsedMilliseconds));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ((CustomersViewModel)BindingContext).Disappear();
    }
}
