using NeedleBase.Application.Contracts.Reports;
using NeedleBase.Application.Reports;
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
        body.Add(Ui.Body("Money in and orders taken, month by month (Accra time). Worked out from the records on this phone."));
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
            await _vm.LoadAsync(_vm.Report?.Month);
            Render();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reports: appear failed: {ex.Message}");
        }
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

        var moneyIn = new Label
        {
            Text = r.MoneyReceivedDisplay,
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Ui.C("Charcoal", Colors.Black),
            AutomationId = "ReportsMoneyIn"
        };
        SemanticProperties.SetDescription(moneyIn, $"Money in: {r.MoneyReceivedDisplay}");
        stack.Add(Ui.Card(new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                Ui.Body("Money in"),
                moneyIn,
                Ui.Body($"{r.PaymentCount} {(r.PaymentCount == 1 ? "payment" : "payments")} recorded · {r.ChangeDisplay}")
            }
        }));

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
        stack.Add(Ui.Body(ReportRules.CostsNote));

        stack.Add(Ui.Heading("How customers paid", 2));
        stack.Add(Ui.Card(r.ByMethod.Count == 0
            ? Ui.Body("No payments recorded this month.")
            : Rows(r.ByMethod.Select(m => Row($"{m.MethodDisplay} ({m.Count})", m.AmountDisplay)))));

        stack.Add(Ui.Heading($"Last {r.Trend.Count} months", 2));
        stack.Add(Ui.Card(Rows(r.Trend.Select(t =>
            Row(t.Label, $"{t.MoneyReceivedDisplay}\n{t.OrdersTaken} {(t.OrdersTaken == 1 ? "order" : "orders")} · {t.OrdersTakenValueDisplay}")))));

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
