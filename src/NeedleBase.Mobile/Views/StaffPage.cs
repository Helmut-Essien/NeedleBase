using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>Staff (online only). Owner: invite, share code, change role, turn access off/on. Manager: view.</summary>
public sealed class StaffPage : ContentPage
{
    private readonly StaffViewModel _vm;
    private readonly IAuthService _auth;

    public StaffPage(StaffViewModel vm, IAuthService auth)
    {
        _vm = vm;
        _auth = auth;
        BindingContext = vm;
        vm.Confirm = (title, message) => DisplayAlert(title, message, "Yes", "No");
        vm.ChooseAction = async (title, actions) =>
        {
            var choice = await DisplayActionSheet(title, "Cancel", null, actions);
            return choice == "Cancel" ? null : choice;
        };
        vm.ShareText = text => Share.Default.RequestAsync(new ShareTextRequest { Title = "Share invite", Text = text });

        var body = new VerticalStackLayout { Spacing = 8 };
        body.Add(Ui.Heading("Staff"));
        body.Add(Ui.Body("Staff changes need a connection."));
        body.Add(Ui.Banner(nameof(StaffViewModel.ErrorMessage), nameof(StaffViewModel.HasError), error: true));
        body.Add(Ui.Banner(nameof(StaffViewModel.StatusMessage), nameof(StaffViewModel.HasStatus), error: false));

        var busy = new ActivityIndicator { Color = Ui.C("Primary", Colors.Green), HeightRequest = 32 };
        busy.SetBinding(ActivityIndicator.IsRunningProperty, nameof(StaffViewModel.IsBusy));
        busy.SetBinding(IsVisibleProperty, nameof(StaffViewModel.IsBusy));
        body.Add(busy);

        body.Add(BuildNewCodeCard());

        var team = new VerticalStackLayout { Spacing = 0 };
        BindableLayout.SetItemsSource(team, vm.Members);
        BindableLayout.SetItemTemplate(team, new DataTemplate(BuildMemberRow));
        body.Add(Ui.Heading("Team", 2));
        body.Add(Ui.Card(team));

        var solo = Ui.Body("It's just you for now. Invite someone to help: they'll get a code to join on their own phone.");
        solo.SetBinding(IsVisibleProperty, nameof(StaffViewModel.IsSolo));
        body.Add(solo);

        var invitesHeading = Ui.Heading("Invites", 2);
        invitesHeading.SetBinding(IsVisibleProperty, nameof(StaffViewModel.HasInvites));
        var invites = new VerticalStackLayout { Spacing = 0 };
        BindableLayout.SetItemsSource(invites, vm.Invites);
        BindableLayout.SetItemTemplate(invites, new DataTemplate(BuildInviteRow));
        var invitesCard = Ui.Card(invites);
        invitesCard.SetBinding(IsVisibleProperty, nameof(StaffViewModel.HasInvites));
        body.Add(invitesHeading);
        body.Add(invitesCard);

        body.Add(BuildInviteForm());
        Ui.Chrome(this, "Staff", body, backButton: true);
    }

    private View BuildNewCodeCard()
    {
        var stack = new VerticalStackLayout { Spacing = 6 };
        stack.Add(Ui.Heading("Invite code", 2));
        var who = Ui.Body();
        who.SetBinding(Label.TextProperty, nameof(StaffViewModel.NewCodeNote));
        stack.Add(who);
        var code = new Label { FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("PrimaryDark", Colors.DarkGreen), FontFamily = "monospace", HorizontalOptions = LayoutOptions.Center };
        code.SetBinding(Label.TextProperty, nameof(StaffViewModel.NewCode));
        SemanticProperties.SetDescription(code, "Invite code");
        stack.Add(code);
        var share = Ui.Primary("Share code (WhatsApp, SMS…)", nameof(StaffViewModel.ShareCodeCommand));
        share.Clicked += async (_, _) =>
        {
            var user = await _auth.GetCachedUserAsync();
            await _vm.ShareCodeCommand.ExecuteAsync(user?.BusinessName);
        };
        share.RemoveBinding(Button.CommandProperty);
        stack.Add(share);
        var done = Ui.Secondary("Done");
        done.SetBinding(Button.CommandProperty, nameof(StaffViewModel.DismissCodeCommand));
        stack.Add(done);
        var card = Ui.Card(stack);
        card.SetBinding(IsVisibleProperty, nameof(StaffViewModel.HasNewCode));
        return card;
    }

    private View BuildInviteForm()
    {
        var form = new VerticalStackLayout { Spacing = 2 };
        form.Add(Ui.Heading("Invite someone", 2));
        form.Add(Ui.FieldLabel("Their email"));
        form.Add(Ui.Field(Ui.Entry("name@example.com", nameof(StaffViewModel.InviteEmail), "Email of the person to invite", Keyboard.Email)));
        form.Add(Ui.FieldLabel("Their name (optional)"));
        form.Add(Ui.Field(Ui.Entry("e.g. Ama Mensah", nameof(StaffViewModel.InviteName), "Name of the person to invite", Keyboard.Text)));
        form.Add(Ui.FieldLabel("Role"));
        var role = new Picker { Title = "Role", TextColor = Ui.C("Charcoal", Colors.Black), ItemsSource = _vm.RoleChoices.ToList() };
        role.SetBinding(Picker.SelectedItemProperty, nameof(StaffViewModel.InviteRole));
        SemanticProperties.SetDescription(role, "Role for the person you're inviting");
        form.Add(Ui.Field(role));
        var summary = Ui.Body();
        summary.SetBinding(Label.TextProperty, nameof(StaffViewModel.InviteRoleSummary));
        form.Add(summary);
        var send = Ui.Primary("Create invite code", nameof(StaffViewModel.SendInviteCommand));
        send.AutomationId = "StaffSendInvite";
        form.Add(send);
        var card = Ui.Card(form);
        card.SetBinding(IsVisibleProperty, nameof(StaffViewModel.CanManage));
        return card;
    }

    private View BuildMemberRow()
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Padding = new Thickness(0, 8),
            MinimumHeightRequest = 56
        };
        var texts = new VerticalStackLayout();
        var name = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("Charcoal", Colors.Black) };
        name.SetBinding(Label.TextProperty, nameof(StaffRow.Name));
        var role = Ui.Body();
        role.SetBinding(Label.TextProperty, nameof(StaffRow.RoleText));
        var work = Ui.Body();
        work.SetBinding(Label.TextProperty, nameof(StaffRow.WorkText));
        texts.Add(name);
        texts.Add(role);
        texts.Add(work);
        grid.Add(texts, 0);
        var manage = Ui.Secondary("Change");
        manage.VerticalOptions = LayoutOptions.Center;
        manage.SetBinding(IsVisibleProperty, nameof(StaffRow.CanChange));
        manage.SetBinding(Button.CommandParameterProperty, ".");
        manage.Command = _vm.ManageCommand;
        SemanticProperties.SetDescription(manage, "Change role or access");
        grid.Add(manage, 1);
        grid.SetBinding(SemanticProperties.DescriptionProperty, nameof(StaffRow.Description));
        return grid;
    }

    private View BuildInviteRow()
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Padding = new Thickness(0, 8),
            MinimumHeightRequest = 56
        };
        var texts = new VerticalStackLayout();
        var title = new Label { FontSize = 15, TextColor = Ui.C("Charcoal", Colors.Black) };
        title.SetBinding(Label.TextProperty, nameof(InviteRow.Title));
        var detail = Ui.Body();
        detail.SetBinding(Label.TextProperty, nameof(InviteRow.Detail));
        texts.Add(title);
        texts.Add(detail);
        grid.Add(texts, 0);
        var revoke = Ui.Secondary("Cancel");
        revoke.VerticalOptions = LayoutOptions.Center;
        revoke.SetBinding(IsVisibleProperty, nameof(InviteRow.CanRevoke));
        revoke.SetBinding(Button.CommandParameterProperty, ".");
        revoke.Command = _vm.RevokeCommand;
        SemanticProperties.SetDescription(revoke, "Cancel this invite");
        grid.Add(revoke, 1);
        return grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Navigation.NavigationStack.LastOrDefault() != this)
        {
            _vm.DismissCodeCommand.Execute(null); // a code is shown once; never left on screen behind other pages
        }
    }
}
