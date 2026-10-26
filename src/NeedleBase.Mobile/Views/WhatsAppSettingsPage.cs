using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>WhatsApp settings and message log (online only). Owner/Manager.</summary>
public sealed class WhatsAppSettingsPage : ContentPage
{
    private readonly WhatsAppSettingsViewModel _vm;

    public WhatsAppSettingsPage(WhatsAppSettingsViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        var body = new VerticalStackLayout { Spacing = 8 };
        body.Add(Ui.Heading("WhatsApp"));
        body.Add(Ui.Body("Automatic order updates go only to customers who agreed (set on each customer). Needs a connection."));
        body.Add(Ui.Banner(nameof(vm.ErrorMessage), nameof(vm.HasError), error: true));
        body.Add(Ui.Banner(nameof(vm.StatusMessage), nameof(vm.HasStatus), error: false));

        var retry = Ui.Secondary("Try again");
        retry.SetBinding(Button.CommandProperty, nameof(vm.RetryCommand));
        retry.SetBinding(IsVisibleProperty, new Binding(nameof(vm.ShowSettings), converter: new InvertBool()));
        body.Add(retry);

        var busy = new ActivityIndicator { Color = Ui.C("Primary", Colors.Green), HeightRequest = 32 };
        busy.SetBinding(ActivityIndicator.IsRunningProperty, nameof(vm.IsBusy));
        busy.SetBinding(IsVisibleProperty, nameof(vm.IsBusy));
        body.Add(busy);

        var notConfigured = Ui.Body("Automatic messages aren't set up on the server yet. You can still send updates yourself from an order (Message on WhatsApp).");
        notConfigured.SetBinding(IsVisibleProperty, nameof(vm.NotConfigured));
        body.Add(notConfigured);
        var readOnly = Ui.Body("Only an owner or manager can change these settings.");
        readOnly.SetBinding(IsVisibleProperty, nameof(vm.ReadOnly));
        body.Add(readOnly);

        var settings = new VerticalStackLayout { Spacing = 6 };
        var onRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, MinimumHeightRequest = 48 };
        onRow.Add(new Label { Text = "Send automatic updates", FontSize = 16, VerticalOptions = LayoutOptions.Center, TextColor = Ui.C("Charcoal", Colors.Black) }, 0);
        var toggle = new Switch { VerticalOptions = LayoutOptions.Center };
        toggle.SetBinding(Switch.IsToggledProperty, nameof(vm.Enabled));
        toggle.SetBinding(IsEnabledProperty, nameof(vm.CanSave));
        SemanticProperties.SetDescription(toggle, "Send automatic WhatsApp updates");
        onRow.Add(toggle, 1);
        settings.Add(onRow);
        var statuses = new VerticalStackLayout { Spacing = 0 };
        BindableLayout.SetItemsSource(statuses, vm.Statuses);
        BindableLayout.SetItemTemplate(statuses, new DataTemplate(() => BuildStatusRow(vm)));
        settings.Add(statuses);
        var save = Ui.Primary("Save", nameof(vm.SaveCommand));
        save.SetBinding(IsEnabledProperty, nameof(vm.CanSave));
        save.SetBinding(IsVisibleProperty, nameof(vm.CanEdit));
        settings.Add(save);
        var card = Ui.Card(settings);
        card.SetBinding(IsVisibleProperty, nameof(vm.ShowSettings));
        body.Add(card);

        var logHeading = Ui.Heading("Message log", 2);
        logHeading.SetBinding(IsVisibleProperty, nameof(vm.ShowSettings));
        body.Add(logHeading);
        var empty = Ui.Body("No automatic messages yet.");
        empty.SetBinding(IsVisibleProperty, nameof(vm.ShowEmptyLog));
        body.Add(empty);
        var log = new VerticalStackLayout { Spacing = 0 };
        BindableLayout.SetItemsSource(log, vm.Messages);
        BindableLayout.SetItemTemplate(log, new DataTemplate(BuildMessageRow));
        var logCard = Ui.Card(log);
        logCard.SetBinding(IsVisibleProperty, nameof(vm.HasMessages));
        body.Add(logCard);
        var more = Ui.Secondary("Show more");
        more.SetBinding(Button.CommandProperty, nameof(vm.LoadMoreCommand));
        more.SetBinding(IsVisibleProperty, nameof(vm.HasMore));
        body.Add(more);
        Ui.Chrome(this, "WhatsApp", body, backButton: true);
    }

    private static View BuildStatusRow(WhatsAppSettingsViewModel vm)
    {
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, MinimumHeightRequest = 48 };
        var box = new CheckBox { VerticalOptions = LayoutOptions.Center };
        box.SetBinding(CheckBox.IsCheckedProperty, nameof(WhatsAppStatusChoice.IsChecked));
        box.SetBinding(IsEnabledProperty, new MultiBinding
        {
            Bindings =
            {
                new Binding(nameof(WhatsAppStatusChoice.Available)),
                new Binding(nameof(vm.CanSave), source: vm)
            },
            Converter = new AllTrue()
        });
        var label = new Label { FontSize = 15, VerticalOptions = LayoutOptions.Center, TextColor = Ui.C("Charcoal", Colors.Black) };
        label.SetBinding(Label.TextProperty, nameof(WhatsAppStatusChoice.Label));
        box.SetBinding(SemanticProperties.DescriptionProperty, nameof(WhatsAppStatusChoice.Label));
        grid.Add(box, 0);
        grid.Add(label, 1);
        return grid;
    }

    private static View BuildMessageRow()
    {
        var stack = new VerticalStackLayout { Padding = new Thickness(0, 8) };
        var title = new Label { FontSize = 15, TextColor = Ui.C("Charcoal", Colors.Black) };
        title.SetBinding(Label.TextProperty, nameof(WhatsAppMessageRow.Title));
        var detail = Ui.Body();
        detail.SetBinding(Label.TextProperty, nameof(WhatsAppMessageRow.Detail));
        var error = new Label { FontSize = 13, TextColor = Ui.C("SemanticError", Colors.DarkRed) };
        error.SetBinding(Label.TextProperty, nameof(WhatsAppMessageRow.Error));
        error.SetBinding(IsVisibleProperty, nameof(WhatsAppMessageRow.HasError));
        stack.Add(title);
        stack.Add(detail);
        stack.Add(error);
        return stack;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _vm.LoadAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WhatsApp settings load failed: {ex.Message}");
        }
    }

    private sealed class InvertBool : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;
    }

    private sealed class AllTrue : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => values.All(v => v is true);
        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture) => [];
    }
}
