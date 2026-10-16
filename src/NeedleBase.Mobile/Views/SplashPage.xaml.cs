using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public partial class SplashPage : ContentPage
{
    private SplashViewModel? _viewModel;
    private bool _initialized;

    public SplashPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel ??= ServiceHelper.GetRequiredService<SplashViewModel>();
        BindingContext = _viewModel;

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _viewModel.InitializeAsync();
    }
}
