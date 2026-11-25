using NeedleBase.Domain.Common;
using NeedleBase.Domain.Entities;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>Free → online with a licence key; keeps records already on this phone.</summary>
public sealed class FreeUpgradePage : ContentPage
{
    private readonly PasswordField _password;
    private readonly PasswordField _confirm;

    public FreeUpgradePage(FreeUpgradeViewModel vm)
    {
        BindingContext = vm;
        Shell.SetNavBarIsVisible(this, false);
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = Ui.C("AppBackground", Colors.White);

        var email = Ui.Entry("you@business.com", nameof(FreeUpgradeViewModel.Email), "Email", Keyboard.Email);
        email.AutomationId = "FreeUpgradeEmail";
        email.MaxLength = Invitation.EmailMax;

        _password = new PasswordField(
            "Choose a password", nameof(FreeUpgradeViewModel.Password), "Password", "FreeUpgradePassword");
        _confirm = new PasswordField(
            "Confirm password", nameof(FreeUpgradeViewModel.ConfirmPassword), "Confirm password", "FreeUpgradeConfirmPassword");

        var first = Ui.Entry("First name", nameof(FreeUpgradeViewModel.FirstName), "First name");
        first.MaxLength = Invitation.NameMax;
        var surname = Ui.Entry("Surname", nameof(FreeUpgradeViewModel.Surname), "Surname");
        surname.MaxLength = Invitation.NameMax;
        var shop = Ui.Entry("Shop name", nameof(FreeUpgradeViewModel.BusinessName), "Shop name");
        shop.AutomationId = "FreeUpgradeShopName";
        shop.MaxLength = FieldLimits.CustomerName;
        var phone = Ui.Entry("+233… (optional)", nameof(FreeUpgradeViewModel.BusinessPhone), "Business phone optional", Keyboard.Telephone);
        phone.MaxLength = FieldLimits.Phone;
        var key = Ui.Entry("Licence key from your welcome email", nameof(FreeUpgradeViewModel.LicenseKey), "Licence key");
        key.AutomationId = "FreeUpgradeLicenseKey";
        key.MaxLength = 128;

        var error = Ui.Body();
        error.TextColor = Ui.C("SemanticError", Colors.Red);
        error.SetBinding(Label.TextProperty, nameof(FreeUpgradeViewModel.ErrorMessage));
        error.SetBinding(IsVisibleProperty, nameof(FreeUpgradeViewModel.HasError));

        var termsError = Ui.Body();
        termsError.TextColor = Ui.C("SemanticError", Colors.Red);
        termsError.SetBinding(Label.TextProperty, nameof(FreeUpgradeViewModel.TermsError));
        termsError.SetBinding(IsVisibleProperty, nameof(FreeUpgradeViewModel.HasTermsError));

        var passwordError = Ui.Body();
        passwordError.TextColor = Ui.C("SemanticError", Colors.Red);
        passwordError.SetBinding(Label.TextProperty, nameof(FreeUpgradeViewModel.PasswordError));
        passwordError.SetBinding(IsVisibleProperty, nameof(FreeUpgradeViewModel.HasPasswordError));

        var confirmError = Ui.Body();
        confirmError.TextColor = Ui.C("SemanticError", Colors.Red);
        confirmError.SetBinding(Label.TextProperty, nameof(FreeUpgradeViewModel.ConfirmPasswordError));
        confirmError.SetBinding(IsVisibleProperty, nameof(FreeUpgradeViewModel.HasConfirmPasswordError));

        var termsCheck = new CheckBox { VerticalOptions = LayoutOptions.Center };
        termsCheck.SetBinding(CheckBox.IsCheckedProperty, nameof(FreeUpgradeViewModel.AcceptTerms));
        SemanticProperties.SetDescription(termsCheck, "Agree to Terms and Conditions");
        var terms = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                termsCheck,
                new Label
                {
                    Text = "I agree to the Terms and Conditions",
                    TextColor = Ui.C("Charcoal", Colors.Black),
                    FontSize = 14,
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };

        var busy = new ActivityIndicator { Color = Ui.C("Primary", Colors.Green) };
        busy.SetBinding(ActivityIndicator.IsRunningProperty, nameof(FreeUpgradeViewModel.IsBusy));
        busy.SetBinding(IsVisibleProperty, nameof(FreeUpgradeViewModel.IsBusy));

        var go = Ui.Primary("Go online and keep my records", nameof(FreeUpgradeViewModel.UpgradeCommand));
        go.AutomationId = "FreeUpgradeSubmit";
        SemanticProperties.SetDescription(go, "Register the online shop with your licence key and keep Free records");
        go.SetBinding(IsEnabledProperty, nameof(FreeUpgradeViewModel.CanSubmit));

        var back = new Button
        {
            Text = "Back",
            BackgroundColor = Colors.Transparent,
            TextColor = Ui.C("PrimaryDark", Colors.DarkGreen),
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            HeightRequest = 48,
            Command = vm.BackCommand
        };
        SemanticProperties.SetDescription(back, "Back to More");

        var body = new VerticalStackLayout
        {
            Padding = new Thickness(16, 40, 16, 28),
            Spacing = 0,
            Children =
            {
                Ui.Heading("Go online"),
                Ui.Body(
                    "Create your NeedleBase account with a licence key. Customers, orders and payments already on this phone move to your online shop and sync when you're connected. Free limits no longer apply."),
                Ui.FieldLabel("Licence key"),
                Ui.Field(key),
                Ui.FieldLabel("Email"),
                Ui.Field(email),
                Ui.FieldLabel("Password"),
                _password,
                passwordError,
                Ui.FieldLabel("Confirm password"),
                _confirm,
                confirmError,
                Ui.FieldLabel("First name"),
                Ui.Field(first),
                Ui.FieldLabel("Surname"),
                Ui.Field(surname),
                Ui.FieldLabel("Shop name"),
                Ui.Field(shop),
                Ui.FieldLabel("Business phone (optional)"),
                Ui.Field(phone),
                terms,
                termsError,
                error,
                busy,
                go,
                back
            }
        };

        Content = new ScrollView { Content = body };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is FreeUpgradeViewModel vm)
        {
            await vm.PrepareAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _password.Remask();
        _confirm.Remask();
    }
}
