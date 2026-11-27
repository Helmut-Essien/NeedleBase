using NeedleBase.Application.Contracts.Reports;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>
/// Profit and monthly reports (plan step 7b): money in, orders taken, still owed, payment methods and the last
/// six months. Rebuilt from the view model on each load (no string bindings).
/// </summary>
public sealed class ReportsPage : ContentPage
{
    private readonly ReportsViewModel _vm;
    private readonly ContentView _slot = new();
    private bool _navigating;

    public ReportsPage(ReportsViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        var body = new VerticalStackLayout { Spacing = 8 };
        body.Add(Ui.Heading("Reports"));
        body.Add(Ui.Body("Money in, money spent and profit, month by month (Accra time). Worked out from the records on this phone."));
        body.Add(_slot);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ReportsViewModel.Report) or nameof(ReportsViewModel.Message))
            {
                Render();
            }
        };
        Ui.Chrome(this, "Reports", body, backButton: true);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _vm.AppearAsync();
            Render();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reports: appear failed: {ex.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Disappear();
    }

    private void Render()
    {
        if (_vm.Message is { } message)
        {
            var text = Ui.Body(message, muted: false);
            text.AutomationId = "ReportsMessage";
            _slot.Content = Ui.Card(text);
            return;
        }

        _slot.Content = _vm.Report is { } r ? Build(r) : null;
    }

    private View Build(MonthlyReportDto r)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };

        var earlier = Ui.Secondary("‹ Earlier");
        earlier.IsEnabled = r.PreviousMonth is not null;
        earlier.AutomationId = "ReportsEarlier";
        earlier.Clicked += (_, _) => Go(_vm.PreviousCommand);
        var later = Ui.Secondary("Later ›");
        later.IsEnabled = r.NextMonth is not null;
        later.AutomationId = "ReportsLater";
        later.Clicked += (_, _) => Go(_vm.NextCommand);
        var month = Ui.Heading(r.MonthLabel + (r.IsCurrentMonth ? " (so far)" : ""), 2);
        month.HorizontalTextAlignment = TextAlignment.Center;
        month.VerticalOptions = LayoutOptions.Center;
        var nav = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8
        };
        nav.Add(earlier, 0);
        nav.Add(month, 1);
        nav.Add(later, 2);
        stack.Add(nav);

        stack.Add(Figure("Money in", r.MoneyReceivedDisplay, "ReportsMoneyIn",
            $"{r.PaymentCount} {(r.PaymentCount == 1 ? "payment" : "payments")} recorded · {r.ChangeDisplay}"));
        stack.Add(Figure("Spent", r.SpentDisplay, "ReportsSpent",
            $"{ExpenseAccess.CountText(r.ExpenseCount)} · {ReportsViewModel.SpentChange(r)}"));
        stack.Add(Figure(r.ProfitLabel, r.ProfitDisplay, "ReportsProfit", r.ProfitChangeDisplay, r.IsLoss));
        // Profit note (expenses plan 6.6): depends on whether the month has expenses.
        stack.Add(Ui.Body(r.ProfitNote));

        stack.Add(Ui.Heading("Spent by category", 2));
        var spentCategories = r.SpentByCategory.Where(c => c.Total > 0).ToList();
        stack.Add(Ui.Card(spentCategories.Count == 0
            ? Ui.Body("No expenses recorded this month.")
            : Rows(spentCategories.Select(c => Bar(c.Label, c.TotalDisplay, c.Count, c.Total, spentCategories.Max(x => x.Total))))));
        var see = Ui.Secondary("See expenses");
        see.AutomationId = "ReportsSeeExpenses";
        SemanticProperties.SetDescription(see, $"See expenses for {r.MonthLabel}");
        see.Clicked += (_, _) => Go(_vm.SeeExpensesCommand);
        stack.Add(see);

        stack.Add(Ui.Card(new VerticalStackLayout
        {
            Children =
            {
                Row($"Orders taken ({r.OrdersTaken})", r.OrdersTakenValueDisplay),
                Ui.Divider(),
                Row($"Still owed on those orders ({r.OrdersStillOwing})", r.StillOwedDisplay),
                Ui.Divider(),
                Row("Cancelled this month", r.OrdersCancelled.ToString(System.Globalization.CultureInfo.InvariantCulture))
            }
        }));

        stack.Add(Ui.Heading("How customers paid", 2));
        stack.Add(Ui.Card(r.ByMethod.Count == 0
            ? Ui.Body("No payments recorded this month.")
            : Rows(r.ByMethod.Select(m => Row($"{m.MethodDisplay} ({m.Count})", m.AmountDisplay)))));

        stack.Add(Ui.Heading($"Last {r.Trend.Count} months", 2));
        stack.Add(Ui.Card(Rows(r.Trend.Select(t =>
            Row(t.Label, $"In {t.MoneyReceivedDisplay} · Spent {t.SpentDisplay}\n{t.ProfitLabel} {t.ProfitDisplay}\n{t.OrdersTaken} {(t.OrdersTaken == 1 ? "order" : "orders")} · {t.OrdersTakenValueDisplay}")))));

        stack.Add(Ui.Heading("Orders taken this month, by status today", 2));
        var statuses = r.OrdersByStatus.Where(s => s.Count > 0).ToList();
        stack.Add(Ui.Card(statuses.Count == 0
            ? Ui.Body("No orders taken this month.")
            : Rows(statuses.Select(s => Row(s.StatusDisplay, s.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))))));
        return stack;
    }

    private async void Go(CommunityToolkit.Mvvm.Input.IAsyncRelayCommand command)
    {
        if (_navigating)
        {
            return;
        }

        _navigating = true;
        try
        {
            await command.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reports: month change failed: {ex.Message}");
        }
        finally
        {
            _navigating = false;
        }
    }

    private static Border Figure(string caption, string value, string automationId, string detail, bool loss = false)
    {
        var amount = new Label
        {
            Text = value,
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = loss ? Ui.C("SemanticOverdue", Colors.DarkRed) : Ui.C("Charcoal", Colors.Black),
            AutomationId = automationId
        };
        var card = Ui.Card(new VerticalStackLayout { Spacing = 4, Children = { Ui.Body(caption), amount, Ui.Body(detail) } });
        SemanticProperties.SetDescription(card, $"{caption}: {value}. {detail}");
        return card;
    }

    /// <summary>Category bar: the text and amount carry the meaning, the bar is decoration.</summary>
    private static View Bar(string label, string amount, int count, decimal total, decimal max)
    {
        var stack = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(0, 8) };
        stack.Add(Row($"{label} ({count})", amount));
        var track = new Grid
        {
            HeightRequest = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(max <= 0 ? 0 : (double)(total / max), GridUnitType.Star)),
                new ColumnDefinition(new GridLength(max <= 0 ? 1 : (double)(1 - total / max), GridUnitType.Star))
            }
        };
        track.Add(new BoxView { Color = Ui.C("PrimaryDark", Colors.DarkGreen), CornerRadius = 4 }, 0);
        track.Add(new BoxView { Color = Ui.C("Gray100", Colors.LightGray), CornerRadius = 4 }, 1);
        stack.Add(Ui.Decorative(track));
        return stack;
    }

    private static VerticalStackLayout Rows(IEnumerable<View> rows)
    {
        var stack = new VerticalStackLayout();
        var first = true;
        foreach (var row in rows)
        {
            if (!first)
            {
                stack.Add(Ui.Divider());
            }

            stack.Add(row);
            first = false;
        }

        return stack;
    }

    private static Grid Row(string label, string value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 12,
            Padding = new Thickness(0, 10),
            MinimumHeightRequest = 44
        };
        var name = Ui.Body(label, muted: false);
        name.VerticalOptions = LayoutOptions.Center;
        var amount = new Label
        {
            Text = value,
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalOptions = LayoutOptions.Center,
            TextColor = Ui.C("Charcoal", Colors.Black)
        };
        grid.Add(name, 0);
        grid.Add(amount, 1);
        SemanticProperties.SetDescription(grid, $"{label}: {value.Replace('\n', ' ')}");
        return grid;
    }
}
