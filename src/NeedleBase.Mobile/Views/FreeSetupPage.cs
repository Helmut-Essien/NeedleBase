using CommunityToolkit.Mvvm.Input;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>Name the Free shop before using the app on this phone only.</summary>
public sealed class FreeSetupPage : ContentPage
{
    public FreeSetupPage(FreeSetupViewModel vm)
    {
        BindingContext = vm;
        Shell.SetNavBarIsVisible(this, false);
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = Ui.C("AppBackground", Colors.White);

        var name = Ui.Entry("e.g. Ama Stitches", nameof(FreeSetupViewModel.ShopName), "Shop name");
        name.AutomationId = "FreeSetupShopName";

        var error = Ui.Body();
        error.TextColor = Ui.C("SemanticError", Colors.Red);
        error.SetBinding(Label.TextProperty, nameof(FreeSetupViewModel.ErrorMessage));
        error.SetBinding(IsVisibleProperty, nameof(FreeSetupViewModel.HasError));

        var busy = new ActivityIndicator { Color = Ui.C("Primary", Colors.Green) };
        busy.SetBinding(ActivityIndicator.IsRunningProperty, nameof(FreeSetupViewModel.IsBusy));
        busy.SetBinding(IsVisibleProperty, nameof(FreeSetupViewModel.IsBusy));

        var start = Ui.Primary("Start Free on this phone", nameof(FreeSetupViewModel.StartCommand));
        start.AutomationId = "FreeSetupStart";
        SemanticProperties.SetDescription(start, "Start Free on this phone with the shop name above");
        start.SetBinding(IsEnabledProperty, nameof(FreeSetupViewModel.CanSubmit));

        var back = new Button
        {
            Text = "Back",
            BackgroundColor = Colors.Transparent,
            TextColor = Ui.C("PrimaryDark", Colors.DarkGreen),
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            HeightRequest = 48,
            Command = new AsyncRelayCommand(async () => await Shell.Current.GoToAsync("//LoginPage"))
        };
        SemanticProperties.SetDescription(back, "Back to welcome");

        var body = new VerticalStackLayout
        {
            Padding = new Thickness(16, 40, 16, 28),
            Spacing = 0,
            Children =
            {
                new Image
                {
                    Source = "needlebase_logo.png",
                    Aspect = Aspect.AspectFit,
                    HeightRequest = 120,
                    WidthRequest = 220,
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 0, 0, 12)
                },
                Ui.Heading("Free on this phone"),
                Ui.Body(
                    "No account and no licence key. Your records stay on this device only — NeedleBase does not sync or back them up. You can keep up to 10 customers and 20 orders ever created; deleting one does not free a slot. Use Save a copy if you want your own backup."),
                Ui.FieldLabel("Shop name"),
                Ui.Field(name),
                error,
                busy,
                start,
                back
            }
        };

        Content = new ScrollView { Content = body };
    }
}
