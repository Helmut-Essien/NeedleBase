using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>One expense (expenses plan section 6.4). "Void expense" asks for a reason, then confirms.</summary>
public sealed class ExpenseDetailPage : ContentPage
{
    private readonly ExpenseDetailViewModel _vm;

    public ExpenseDetailPage(ExpenseDetailViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        vm.Confirm = (title, message, accept, cancel) => DisplayAlert(title, message, accept, cancel);

        var body = new VerticalStackLayout { Spacing = 8 };
        body.Add(Ui.Heading("Expense"));

        var message = Ui.Card(ExpenseUi.Bound(nameof(ExpenseDetailViewModel.Message)));
        message.SetBinding(IsVisibleProperty, nameof(ExpenseDetailViewModel.HasMessage));
        body.Add(message);

        var detail = new VerticalStackLayout { Spacing = 6 };
        detail.SetBinding(IsVisibleProperty, nameof(ExpenseDetailViewModel.HasExpense));

        var amount = ExpenseUi.Bound(nameof(ExpenseDetailViewModel.AmountDisplay), 28, bold: true);
        amount.AutomationId = "ExpenseDetailAmount";
        var struck = new DataTrigger(typeof(Label)) { Binding = new Binding(nameof(ExpenseDetailViewModel.IsVoided)), Value = true };
        struck.Setters.Add(new Setter { Property = Label.TextDecorationsProperty, Value = TextDecorations.Strikethrough });
        struck.Setters.Add(new Setter { Property = Label.TextColorProperty, Value = Ui.C("Gray600", Colors.Gray) });
        amount.Triggers.Add(struck);
        var card = new VerticalStackLayout { Spacing = 6 };
        card.Add(amount);
        card.Add(Labelled("Spent on", nameof(ExpenseDetailViewModel.CategoryLabel), null));
        card.Add(Labelled("Date paid", nameof(ExpenseDetailViewModel.DateDisplay), null));
        card.Add(Labelled("Note", nameof(ExpenseDetailViewModel.NoteText), nameof(ExpenseDetailViewModel.HasNote)));

        var order = Labelled("Order", nameof(ExpenseDetailViewModel.OrderText), nameof(ExpenseDetailViewModel.HasOrder));
        var openOrder = Ui.Secondary("Open order");
        SemanticProperties.SetDescription(openOrder, "Open the linked order");
        openOrder.SetBinding(Button.CommandProperty, nameof(ExpenseDetailViewModel.OpenOrderCommand));
        openOrder.SetBinding(IsVisibleProperty, nameof(ExpenseDetailViewModel.CanOpenOrder));
        card.Add(order);
        card.Add(openOrder);

        card.Add(ExpenseUi.Bound(nameof(ExpenseDetailViewModel.RecordedText), 13, muted: true));
        var pending = ExpenseUi.Badge(ExpenseRow.PendingText, warning: true);
        pending.SetBinding(IsVisibleProperty, nameof(ExpenseDetailViewModel.HasSyncText));
        card.Add(pending);
        detail.Add(Ui.Card(card));

        var voided = new VerticalStackLayout { Spacing = 4 };
        voided.Add(ExpenseUi.Badge("Voided", warning: false));
        voided.Add(ExpenseUi.Bound(nameof(ExpenseDetailViewModel.VoidedText)));
        var voidedCard = Ui.Card(voided);
        voidedCard.SetBinding(IsVisibleProperty, nameof(ExpenseDetailViewModel.IsVoided));
        detail.Add(voidedCard);

        var voidButton = Ui.Secondary("Void expense");
        voidButton.AutomationId = "ExpenseVoid";
        voidButton.SetBinding(Button.CommandProperty, nameof(ExpenseDetailViewModel.StartVoidCommand));
        voidButton.SetBinding(IsVisibleProperty, nameof(ExpenseDetailViewModel.ShowVoidButton));
        detail.Add(voidButton);

        var form = new VerticalStackLayout { Spacing = 4 };
        form.Add(Ui.Heading("Void this expense", 2));
        form.Add(Ui.Body("Voiding keeps it in your records but stops it counting toward profit. Record it again if the details were wrong."));
        form.Add(Ui.FieldLabel("Why are you voiding it?"));
        var reason = Ui.Entry("e.g. Wrong amount", nameof(ExpenseDetailViewModel.VoidReason), "Reason for voiding");
        reason.AutomationId = "ExpenseVoidReason";
        form.Add(Ui.Field(reason));
        form.Add(ExpenseUi.ErrorText(nameof(ExpenseDetailViewModel.VoidError), nameof(ExpenseDetailViewModel.HasVoidError)));
        var confirm = Ui.Primary("Void expense", nameof(ExpenseDetailViewModel.ConfirmVoidCommand));
        confirm.BackgroundColor = Ui.C("SemanticOverdue", Colors.DarkRed);
        form.Add(confirm);
        var cancel = Ui.Secondary("Keep it");
        cancel.SetBinding(Button.CommandProperty, nameof(ExpenseDetailViewModel.CancelVoidCommand));
        form.Add(cancel);
        var formCard = Ui.Card(form);
        formCard.SetBinding(IsVisibleProperty, nameof(ExpenseDetailViewModel.IsVoidFormOpen));
        detail.Add(formCard);

        body.Add(detail);
        Ui.Chrome(this, "Expense", body, backButton: true);
    }

    private static View Labelled(string caption, string path, string? visiblePath)
    {
        var stack = new VerticalStackLayout { Spacing = 0 };
        stack.Add(new Label { Text = caption, FontSize = 13, TextColor = Ui.C("Gray600", Colors.Gray) });
        stack.Add(ExpenseUi.Bound(path, 16));
        if (visiblePath is not null)
        {
            stack.SetBinding(IsVisibleProperty, visiblePath);
        }

        return stack;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _vm.AppearAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Expense detail: appear failed: {ex.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Disappear();
    }
}
