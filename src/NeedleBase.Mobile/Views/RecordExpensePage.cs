using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Common;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>
/// Record expense (expenses plan section 6.3): amount, category, date paid, note, optional order. Back/Cancel ask
/// before dropping what was typed (<see cref="RecordExpenseViewModel.TryHandleBack"/>).
/// </summary>
public sealed class RecordExpensePage : ContentPage
{
    public RecordExpensePage(RecordExpenseViewModel vm)
    {
        BindingContext = vm;
        vm.Confirm = (title, message, accept, cancel) => DisplayAlert(title, message, accept, cancel);

        var body = new VerticalStackLayout { Spacing = 4 };
        var back = new Button
        {
            Text = "‹ Back",
            BackgroundColor = Colors.Transparent,
            TextColor = Ui.C("PrimaryDark", Colors.DarkGreen),
            FontSize = 16,
            HeightRequest = 48,
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Start
        };
        SemanticProperties.SetDescription(back, "Go back");
        back.SetBinding(Button.CommandProperty, nameof(RecordExpenseViewModel.CancelCommand));
        body.Add(back);
        body.Add(Ui.Heading("Record expense"));
        body.Add(Ui.Body("Money the shop paid out. Saved on this phone first, then synced."));

        var blocked = Ui.Card(ExpenseUi.Bound(nameof(RecordExpenseViewModel.BlockedMessage)));
        blocked.SetBinding(IsVisibleProperty, new Binding(nameof(RecordExpenseViewModel.ShowForm), converter: new InvertConverter()));
        body.Add(blocked);

        var form = new VerticalStackLayout { Spacing = 4 };
        form.SetBinding(IsVisibleProperty, nameof(RecordExpenseViewModel.ShowForm));

        form.Add(Ui.FieldLabel("Amount (GH₵)"));
        var amount = Ui.Entry("e.g. 120.00", nameof(RecordExpenseViewModel.AmountText), "Amount paid in Ghana cedis", Keyboard.Numeric);
        amount.AutomationId = "ExpenseAmount";
        form.Add(Ui.Field(new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 6,
            Children = { Ui.Decorative(new Label { Text = "GH₵", FontSize = 16, VerticalOptions = LayoutOptions.Center, TextColor = Ui.C("Gray600", Colors.Gray) }) }
        }.WithChild(amount, 1)));
        form.Add(ExpenseUi.ErrorText(nameof(RecordExpenseViewModel.AmountError), nameof(RecordExpenseViewModel.HasAmountError)));

        form.Add(Ui.FieldLabel("What was it for?"));
        var categories = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start };
        BindableLayout.SetItemsSource(categories, vm.Categories);
        BindableLayout.SetItemTemplate(categories, new DataTemplate(() =>
        {
            var chip = ExpenseUi.Chip(nameof(RecordExpenseViewModel.SelectCategoryCommand));
            chip.Command = vm.SelectCategoryCommand;
            chip.Margin = new Thickness(0, 0, 8, 8);
            return chip;
        }));
        form.Add(categories);
        form.Add(ExpenseUi.ErrorText(nameof(RecordExpenseViewModel.CategoryError), nameof(RecordExpenseViewModel.HasCategoryError)));

        form.Add(Ui.FieldLabel("Date paid"));
        var date = new DatePicker
        {
            Format = "dd MMM yyyy",
            MinimumDate = vm.MinDate,
            MaximumDate = vm.MaxDate,
            TextColor = Ui.C("Charcoal", Colors.Black),
            AutomationId = "ExpenseDate"
        };
        date.SetBinding(DatePicker.DateProperty, nameof(RecordExpenseViewModel.SpentOn), BindingMode.TwoWay);
        SemanticProperties.SetDescription(date, "Date the money was paid");
        form.Add(Ui.Field(date));
        form.Add(ExpenseUi.ErrorText(nameof(RecordExpenseViewModel.DateError), nameof(RecordExpenseViewModel.HasDateError)));

        form.Add(Ui.FieldLabel("Note (optional)"));
        var note = new Editor
        {
            Placeholder = "e.g. Makola lining and zips",
            PlaceholderColor = Ui.C("Gray600", Colors.Gray),
            TextColor = Ui.C("Charcoal", Colors.Black),
            MaxLength = FieldLimits.Notes,
            AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 80,
            AutomationId = "ExpenseNote"
        };
        note.SetBinding(Editor.TextProperty, nameof(RecordExpenseViewModel.Note));
        SemanticProperties.SetDescription(note, "Note about the expense");
        form.Add(Ui.Field(note));
        var counter = ExpenseUi.Bound(nameof(RecordExpenseViewModel.NoteCounter), 12, muted: true);
        counter.HorizontalTextAlignment = TextAlignment.End;
        form.Add(counter);
        form.Add(ExpenseUi.ErrorText(nameof(RecordExpenseViewModel.NoteError), nameof(RecordExpenseViewModel.HasNoteError)));

        form.Add(Ui.FieldLabel("Order (optional)"));
        form.Add(BuildOrder(vm));

        form.Add(Ui.Banner(nameof(RecordExpenseViewModel.ErrorMessage), nameof(RecordExpenseViewModel.HasError), error: true));
        var save = Ui.Primary("Save expense", nameof(RecordExpenseViewModel.SaveCommand));
        save.AutomationId = "ExpenseSave";
        SemanticProperties.SetDescription(save, "Save expense");
        form.Add(save);
        var cancel = Ui.Secondary("Cancel");
        cancel.SetBinding(Button.CommandProperty, nameof(RecordExpenseViewModel.CancelCommand));
        form.Add(cancel);
        body.Add(form);

        Ui.Chrome(this, "Record expense", body, backButton: false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await ((RecordExpenseViewModel)BindingContext).AppearAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Record expense: appear failed: {ex.Message}");
        }
    }

    private static View BuildOrder(RecordExpenseViewModel vm)
    {
        var stack = new VerticalStackLayout { Spacing = 6 };

        var linked = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8
        };
        var label = ExpenseUi.Bound(nameof(RecordExpenseViewModel.OrderLabel), 15, bold: true);
        label.VerticalOptions = LayoutOptions.Center;
        linked.Add(label, 0);
        var remove = Ui.Secondary("Remove link");
        SemanticProperties.SetDescription(remove, "Remove the link to this order");
        remove.SetBinding(Button.CommandProperty, nameof(RecordExpenseViewModel.RemoveOrderCommand));
        linked.Add(remove, 1);
        linked.SetBinding(IsVisibleProperty, nameof(RecordExpenseViewModel.HasOrder));
        stack.Add(linked);

        var link = Ui.Secondary("Link to an order");
        link.SetBinding(Button.CommandProperty, nameof(RecordExpenseViewModel.StartOrderPickCommand));
        link.SetBinding(IsVisibleProperty, new Binding(nameof(RecordExpenseViewModel.HasOrder), converter: new InvertConverter()));
        stack.Add(link);

        var picker = new VerticalStackLayout { Spacing = 4 };
        picker.SetBinding(IsVisibleProperty, nameof(RecordExpenseViewModel.ShowOrderPicker));
        var search = Ui.Entry("Search by order number or customer", nameof(RecordExpenseViewModel.OrderSearch), "Search orders");
        search.ReturnCommand = vm.SearchOrdersCommand;
        picker.Add(Ui.Field(search));
        var find = Ui.Secondary("Search");
        find.SetBinding(Button.CommandProperty, nameof(RecordExpenseViewModel.SearchOrdersCommand));
        picker.Add(find);
        var results = new VerticalStackLayout();
        BindableLayout.SetItemsSource(results, vm.OrderResults);
        BindableLayout.SetItemTemplate(results, new DataTemplate(() =>
        {
            var row = new Button
            {
                BackgroundColor = Colors.White,
                TextColor = Ui.C("Charcoal", Colors.Black),
                HeightRequest = 48,
                HorizontalOptions = LayoutOptions.Fill,
                Command = vm.PickOrderCommand
            };
            row.SetBinding(Button.CommandParameterProperty, ".");
            row.BindingContextChanged += (_, _) =>
            {
                if (row.BindingContext is OrderListItemDto order)
                {
                    row.Text = $"#{order.DisplayRef} · {order.CustomerName}";
                    SemanticProperties.SetDescription(row, $"Link to order {order.DisplayRef} for {order.CustomerName}");
                }
            };
            return row;
        }));
        picker.Add(results);
        stack.Add(picker);
        return stack;
    }

    private sealed class InvertConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

internal static class GridExtensions
{
    internal static Grid WithChild(this Grid grid, View child, int column)
    {
        grid.Add(child, column);
        return grid;
    }
}
