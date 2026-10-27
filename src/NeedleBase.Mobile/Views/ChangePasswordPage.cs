using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public sealed class ChangePasswordPage : ContentPage
{
    private readonly ChangePasswordViewModel _vm;

    public ChangePasswordPage(ChangePasswordViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        var body = new VerticalStackLayout { Spacing = 2 };
        body.Add(Ui.Heading("Change password"));
        body.Add(Ui.Body("Other phones and browsers signed in to your account will be signed out. Needs a connection."));
        body.Add(Ui.Banner(nameof(ChangePasswordViewModel.ErrorMessage), nameof(ChangePasswordViewModel.HasError), error: true));
        body.Add(Ui.Banner(nameof(ChangePasswordViewModel.StatusMessage), nameof(ChangePasswordViewModel.HasStatus), error: false));
        body.Add(Ui.FieldLabel("Current password"));
        var current = new PasswordField("Current password", nameof(ChangePasswordViewModel.CurrentPassword), "Current password", "ChangePasswordCurrent");
        body.Add(current);
        body.Add(Ui.FieldLabel("New password"));
        var next = new PasswordField("New password", nameof(ChangePasswordViewModel.NewPassword), "New password", "ChangePasswordNew");
        body.Add(next);
        body.Add(Ui.Body(PasswordRules.Hint));
        body.Add(Ui.FieldLabel("New password again"));
        var again = new PasswordField("New password again", nameof(ChangePasswordViewModel.ConfirmPassword), "Confirm new password", "ChangePasswordConfirm");
        body.Add(again);
        Disappearing += (_, _) => { current.Remask(); next.Remask(); again.Remask(); };
        var save = Ui.Primary("Change password", nameof(ChangePasswordViewModel.SaveCommand));
        save.AutomationId = "ChangePasswordSave";
        body.Add(save);
        Ui.Chrome(this, "Change password", body, backButton: true);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Clear();
    }
}
