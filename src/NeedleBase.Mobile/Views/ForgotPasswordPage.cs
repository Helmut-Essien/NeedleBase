using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>Forgot password: email step, then code + new password (from the sign-in screen; no tab bar).</summary>
public sealed class ForgotPasswordPage : ContentPage
{
    private readonly ForgotPasswordViewModel _vm;

    public ForgotPasswordPage(ForgotPasswordViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        var body = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(0, 24, 0, 0) };
        body.Add(Ui.Heading("Reset your password"));
        body.Add(Ui.Banner(nameof(ForgotPasswordViewModel.ErrorMessage), nameof(ForgotPasswordViewModel.HasError), error: true));
        body.Add(Ui.Banner(nameof(ForgotPasswordViewModel.StatusMessage), nameof(ForgotPasswordViewModel.HasStatus), error: false));

        var emailStep = new VerticalStackLayout { Spacing = 2 };
        emailStep.Add(Ui.Body("Enter the email you sign in with. We'll email you a code. Needs a connection."));
        emailStep.Add(Ui.FieldLabel("Email"));
        emailStep.Add(Ui.Field(Ui.Entry("you@example.com", nameof(ForgotPasswordViewModel.Email), "Email", Keyboard.Email)));
        var send = Ui.Primary("Email me a code", nameof(ForgotPasswordViewModel.SendCodeCommand));
        send.AutomationId = "ForgotSendCode";
        emailStep.Add(send);
        emailStep.SetBinding(IsVisibleProperty, nameof(ForgotPasswordViewModel.NeedsEmail));
        body.Add(emailStep);

        var codeStep = new VerticalStackLayout { Spacing = 2 };
        codeStep.Add(Ui.FieldLabel("Code from the email"));
        codeStep.Add(Ui.Field(Ui.Entry("1234 5678", nameof(ForgotPasswordViewModel.Code), "8-digit code", Keyboard.Numeric)));
        codeStep.Add(Ui.FieldLabel("New password"));
        var next = new PasswordField("New password", nameof(ForgotPasswordViewModel.NewPassword), "New password", "ResetPasswordNew");
        codeStep.Add(next);
        codeStep.Add(Ui.Body(PasswordRules.Hint));
        codeStep.Add(Ui.FieldLabel("New password again"));
        var again = new PasswordField("New password again", nameof(ForgotPasswordViewModel.ConfirmPassword), "Confirm new password", "ResetPasswordConfirm");
        codeStep.Add(again);
        Disappearing += (_, _) => { next.Remask(); again.Remask(); };
        var reset = Ui.Primary("Set new password", nameof(ForgotPasswordViewModel.ResetCommand));
        reset.AutomationId = "ForgotReset";
        codeStep.Add(reset);
        var other = Ui.Secondary("Use a different email");
        other.Margin = new Thickness(0, 8, 0, 0);
        other.SetBinding(Button.CommandProperty, nameof(ForgotPasswordViewModel.UseDifferentEmailCommand));
        codeStep.Add(other);
        codeStep.SetBinding(IsVisibleProperty, nameof(ForgotPasswordViewModel.CanEnterCode));
        body.Add(codeStep);

        var signIn = new Button
        {
            Text = "Back to sign in",
            BackgroundColor = Colors.Transparent,
            TextColor = Ui.C("PrimaryDark", Colors.DarkGreen),
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 48,
            Margin = new Thickness(0, 12, 0, 0),
            AutomationId = "ForgotBackToSignIn"
        };
        signIn.SetBinding(IsEnabledProperty, nameof(BaseViewModel.CanSubmit));
        signIn.Clicked += async (_, _) =>
        {
            _vm.Reset();
            await Shell.Current.GoToAsync("//LoginPage");
        };
        body.Add(signIn);
        Ui.Chrome(this, "Reset password", body, backButton: false);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_vm.CanEnterCode)
        {
            _vm.Reset(); // keep the code step if the user just switched to the mail app
        }
    }
}
