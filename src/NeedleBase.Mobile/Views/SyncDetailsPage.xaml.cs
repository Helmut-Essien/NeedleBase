using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class SyncDetailsPage : ContentPage
{
    public SyncDetailsPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<SyncStatusViewModel>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is SyncStatusViewModel vm)
        {
            await vm.LoadItemsCommand.ExecuteAsync(null);
        }
    }
}
