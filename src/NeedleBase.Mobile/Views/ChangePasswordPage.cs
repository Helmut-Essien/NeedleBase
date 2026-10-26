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
        body.Add(Ui.Field(Ui.Entry("Current password", nameof(ChangePasswordViewModel.CurrentPassword), "Current password", password: true)));
        body.Add(Ui.FieldLabel("New password"));
        body.Add(Ui.Field(Ui.Entry("New password", nameof(ChangePasswordViewModel.NewPassword), "New password", password: true)));
        body.Add(Ui.Body(PasswordRules.Hint));
        body.Add(Ui.FieldLabel("New password again"));
        body.Add(Ui.Field(Ui.Entry("New password again", nameof(ChangePasswordViewModel.ConfirmPassword), "Confirm new password", password: true)));
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
