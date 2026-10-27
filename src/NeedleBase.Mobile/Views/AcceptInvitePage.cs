using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>Join a business with an invite code (from the sign-in screen; no tab bar).</summary>
public sealed class AcceptInvitePage : ContentPage
{
    public AcceptInvitePage(AcceptInviteViewModel vm)
    {
        BindingContext = vm;
        var body = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(0, 24, 0, 0) };
        body.Add(Ui.Heading("Join your team"));
        var heading = Ui.Body(muted: false);
        heading.SetBinding(Label.TextProperty, nameof(AcceptInviteViewModel.Heading));
        body.Add(heading);
        var summary = Ui.Body();
        summary.SetBinding(Label.TextProperty, nameof(AcceptInviteViewModel.RoleSummary));
        summary.SetBinding(IsVisibleProperty, nameof(AcceptInviteViewModel.HasPreview));
        body.Add(summary);
        body.Add(Ui.Banner(nameof(AcceptInviteViewModel.ErrorMessage), nameof(AcceptInviteViewModel.HasError), error: true));

        var codeStep = new VerticalStackLayout { Spacing = 2 };
        codeStep.Add(Ui.FieldLabel("Invite code"));
        var code = Ui.Entry("ABCD-EFGH-JKMN", nameof(AcceptInviteViewModel.Code), "Invite code");
        code.Keyboard = Keyboard.Create(KeyboardFlags.CapitalizeCharacter);
        codeStep.Add(Ui.Field(code));
        codeStep.Add(Ui.Primary("Continue", nameof(AcceptInviteViewModel.CheckCommand)));
        codeStep.SetBinding(IsVisibleProperty, nameof(AcceptInviteViewModel.NeedsCode));
        body.Add(codeStep);

        var joinStep = new VerticalStackLayout { Spacing = 2 };
        joinStep.Add(Ui.FieldLabel("First name"));
        joinStep.Add(Ui.Field(Ui.Entry("First name", nameof(AcceptInviteViewModel.FirstName), "First name", Keyboard.Create(KeyboardFlags.CapitalizeWord))));
        joinStep.Add(Ui.FieldLabel("Surname"));
        joinStep.Add(Ui.Field(Ui.Entry("Surname", nameof(AcceptInviteViewModel.Surname), "Surname", Keyboard.Create(KeyboardFlags.CapitalizeWord))));
        joinStep.Add(Ui.FieldLabel("Choose a password"));
        var choose = new PasswordField("Password", nameof(AcceptInviteViewModel.Password), "Choose a password", "AcceptInvitePassword");
        joinStep.Add(choose);
        Disappearing += (_, _) => choose.Remask();
        joinStep.Add(Ui.Body(PasswordRules.Hint));
        var join = Ui.Primary("Join", nameof(AcceptInviteViewModel.JoinCommand));
        join.AutomationId = "AcceptInviteJoin";
        joinStep.Add(join);
        var other = Ui.Secondary("Use a different code");
        other.Margin = new Thickness(0, 8, 0, 0);
        other.SetBinding(Button.CommandProperty, nameof(AcceptInviteViewModel.UseDifferentCodeCommand));
        joinStep.Add(other);
        joinStep.SetBinding(IsVisibleProperty, nameof(AcceptInviteViewModel.HasPreview));
        body.Add(joinStep);

        var signIn = new Button
        {
            Text = "Back to sign in",
            BackgroundColor = Colors.Transparent,
            TextColor = Ui.C("PrimaryDark", Colors.DarkGreen),
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 48,
            Margin = new Thickness(0, 12, 0, 0)
        };
        signIn.SetBinding(IsEnabledProperty, nameof(BaseViewModel.CanSubmit));
        signIn.Clicked += async (_, _) =>
        {
            vm.Reset();
            await Shell.Current.GoToAsync("//LoginPage");
        };
        body.Add(signIn);
        Ui.Chrome(this, "Join your team", body, backButton: false);
    }
}
