using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

public sealed class DeleteAccountPage : ContentPage
{
    private readonly DeleteAccountViewModel _vm;

    public DeleteAccountPage(DeleteAccountViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        var body = new VerticalStackLayout { Spacing = 2 };
        body.Add(Ui.Heading("Delete account"));
        var warning = Ui.Body("");
        warning.SetBinding(Label.TextProperty, nameof(DeleteAccountViewModel.Warning));
        warning.TextColor = Ui.C("SemanticOverdue", Colors.DarkRed);
        body.Add(warning);
        body.Add(Ui.Banner(nameof(DeleteAccountViewModel.ErrorMessage), nameof(DeleteAccountViewModel.HasError), error: true));
        body.Add(Ui.FieldLabel("Password"));
        var password = new PasswordField("Password", nameof(DeleteAccountViewModel.Password), "Password", "DeleteAccountPassword");
        body.Add(password);
        var confirmLabel = Ui.FieldLabel("Type DELETE to confirm");
        confirmLabel.SetBinding(Label.TextProperty, nameof(DeleteAccountViewModel.ConfirmationLabel));
        body.Add(confirmLabel);
        var confirm = Ui.Entry("Confirmation", nameof(DeleteAccountViewModel.Confirmation), "Type the confirmation words");
        confirm.AutomationId = "DeleteAccountConfirmation";
        body.Add(Ui.Field(confirm));
        Disappearing += (_, _) => password.Remask();
        var submit = Ui.Primary("Delete my account", nameof(DeleteAccountViewModel.DeleteCommand));
        submit.AutomationId = "DeleteAccountSubmit";
        submit.BackgroundColor = Ui.C("SemanticOverdue", Colors.DarkRed);
        body.Add(submit);
        Ui.Chrome(this, "Delete account", body, backButton: true);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Clear();
    }
}
