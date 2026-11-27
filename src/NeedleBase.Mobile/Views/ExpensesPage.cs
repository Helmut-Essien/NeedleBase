using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>
/// Expenses (expenses plan section 6.2): month switcher, summary, category chips, list, Record expense.
/// Opened from More → Money, Reports ("See expenses", with <c>month</c>) or an order ("See all", with <c>orderId</c>).
/// </summary>
public sealed class ExpensesPage : ContentPage
{
    private readonly ExpensesViewModel _vm;

    public ExpensesPage(ExpensesViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;

        var body = new VerticalStackLayout { Spacing = 8 };
        body.Add(Ui.Heading("Expenses"));
        body.Add(Ui.Body(ExpenseAccess.Subtitle));

        var blocked = Ui.Card(ExpenseUi.Bound(nameof(ExpensesViewModel.Message)));
        blocked.AutomationId = "ExpensesMessage";
        blocked.SetBinding(IsVisibleProperty, nameof(ExpensesViewModel.IsBlocked));
        body.Add(blocked);

        var readOnly = Ui.Banner(nameof(ExpensesViewModel.ReadOnlyMessage), nameof(ExpensesViewModel.IsReadOnly), error: true);
        body.Add(readOnly);
        body.Add(Ui.Banner(nameof(ExpensesViewModel.OfflineMessage), nameof(ExpensesViewModel.IsOffline), error: false));
        body.Add(Ui.Banner(nameof(ExpensesViewModel.ErrorMessage), nameof(ExpensesViewModel.HasError), error: true));

        var content = new VerticalStackLayout { Spacing = 8 };
        content.SetBinding(IsVisibleProperty, nameof(ExpensesViewModel.ShowContent));
        content.Add(BuildMonthSwitcher());

        var orderTitle = ExpenseUi.Bound(nameof(ExpensesViewModel.OrderTitle), 17, bold: true);
        orderTitle.SetBinding(IsVisibleProperty, new Binding(nameof(ExpensesViewModel.OrderId), converter: new NotNullConverter()));
        content.Add(orderTitle);

        var summary = ExpenseUi.Bound(nameof(ExpensesViewModel.SummaryText), 16, bold: true);
        summary.AutomationId = "ExpensesSummary";
        content.Add(Ui.Card(summary));

        var chips = new HorizontalStackLayout { Spacing = 8 };
        BindableLayout.SetItemsSource(chips, vm.Chips);
        BindableLayout.SetItemTemplate(chips, new DataTemplate(() =>
        {
            var chip = ExpenseUi.Chip(nameof(ExpensesViewModel.SelectCategoryCommand));
            chip.Command = vm.SelectCategoryCommand;
            return chip;
        }));
        content.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Never });

        var record = Ui.Primary("Record expense", nameof(ExpensesViewModel.RecordCommand));
        record.AutomationId = "ExpensesRecord";
        SemanticProperties.SetDescription(record, "Record an expense");
        record.SetBinding(IsVisibleProperty, nameof(ExpensesViewModel.CanRecord));
        content.Add(record);

        var empty = ExpenseUi.Bound(nameof(ExpensesViewModel.EmptyText), muted: true);
        empty.AutomationId = "ExpensesEmpty";
        empty.SetBinding(IsVisibleProperty, nameof(ExpensesViewModel.ShowEmpty));
        content.Add(empty);

        var list = new VerticalStackLayout();
        BindableLayout.SetItemsSource(list, vm.Items);
        BindableLayout.SetItemTemplate(list, new DataTemplate(() => ExpenseUi.Row(row => vm.OpenCommand.ExecuteAsync(row))));
        var listCard = Ui.Card(list);
        listCard.SetBinding(IsVisibleProperty, new Binding(nameof(ExpensesViewModel.ShowEmpty), converter: new InvertConverter()));
        content.Add(listCard);
        body.Add(content);

        Ui.Chrome(this, "Expenses", body, backButton: true);
        var refresh = new RefreshView { Content = Content, RefreshColor = Ui.C("Primary", Colors.Green) };
        refresh.SetBinding(RefreshView.IsRefreshingProperty, nameof(ExpensesViewModel.IsRefreshing));
        refresh.SetBinding(RefreshView.CommandProperty, nameof(ExpensesViewModel.RefreshCommand));
        Content = refresh;
    }

    private View BuildMonthSwitcher()
    {
        var earlier = Ui.Secondary("‹");
        earlier.AutomationId = "ExpensesEarlier";
        SemanticProperties.SetDescription(earlier, "Earlier month");
        earlier.SetBinding(Button.CommandProperty, nameof(ExpensesViewModel.PreviousCommand));
        earlier.SetBinding(IsEnabledProperty, nameof(ExpensesViewModel.CanGoPrevious));
        var later = Ui.Secondary("›");
        later.AutomationId = "ExpensesLater";
        SemanticProperties.SetDescription(later, "Later month");
        later.SetBinding(Button.CommandProperty, nameof(ExpensesViewModel.NextCommand));
        later.SetBinding(IsEnabledProperty, nameof(ExpensesViewModel.CanGoNext));
        var month = Ui.Heading(string.Empty, 2);
        month.SetBinding(Label.TextProperty, nameof(ExpensesViewModel.MonthLabel));
        month.HorizontalTextAlignment = TextAlignment.Center;
        month.VerticalOptions = LayoutOptions.Center;
        var nav = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(new GridLength(56)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(56)) },
            ColumnSpacing = 8
        };
        nav.Add(earlier, 0);
        nav.Add(month, 1);
        nav.Add(later, 2);
        nav.SetBinding(IsVisibleProperty, nameof(ExpensesViewModel.ShowMonthSwitcher));
        return nav;
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
            System.Diagnostics.Debug.WriteLine($"Expenses: appear failed: {ex.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Disappear();
    }

    private sealed class NotNullConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not null;

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class InvertConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
