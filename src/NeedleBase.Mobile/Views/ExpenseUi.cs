using MauiIcons.Core;
using MauiIcons.Material;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>Shared builders for the expense screens (status = colour + text + icon; 48 dp targets).</summary>
internal static class ExpenseUi
{
    internal static MaterialIcons Icon(string? category) => category switch
    {
        ExpenseCategory.Materials => MaterialIcons.Checkroom,
        ExpenseCategory.StaffPay => MaterialIcons.Payments,
        ExpenseCategory.RentAndBills => MaterialIcons.Home,
        ExpenseCategory.Equipment => MaterialIcons.Build,
        ExpenseCategory.Transport => MaterialIcons.DirectionsBus,
        _ => MaterialIcons.MoreHoriz
    };

    /// <summary>A chip button; filled when its <see cref="CategoryChip.IsSelected"/> is true.</summary>
    internal static Button Chip(string command)
    {
        var button = new Button
        {
            FontSize = 14,
            CornerRadius = 18,
            HeightRequest = 48,
            Padding = new Thickness(14, 0),
            BorderWidth = 1,
            BorderColor = Ui.C("PrimaryDark", Colors.DarkGreen),
            BackgroundColor = Colors.White,
            TextColor = Ui.C("PrimaryDark", Colors.DarkGreen)
        };
        button.SetBinding(Button.TextProperty, nameof(CategoryChip.Label));
        button.SetBinding(Button.CommandParameterProperty, nameof(CategoryChip.Code));
        var selected = new DataTrigger(typeof(Button)) { Binding = new Binding(nameof(CategoryChip.IsSelected)), Value = true };
        selected.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = Ui.C("PrimaryDark", Colors.DarkGreen) });
        selected.Setters.Add(new Setter { Property = Button.TextColorProperty, Value = Colors.White });
        selected.Setters.Add(new Setter { Property = Button.FontAttributesProperty, Value = FontAttributes.Bold });
        button.Triggers.Add(selected);
        // Selected state in words too (not colour only).
        button.BindingContextChanged += (_, _) =>
        {
            if (button.BindingContext is CategoryChip chip)
            {
                void Describe() => SemanticProperties.SetDescription(button, $"{chip.Label}{(chip.IsSelected ? ", selected" : string.Empty)}");
                Describe();
                chip.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(CategoryChip.IsSelected)) Describe();
                };
            }
        };
        return button;
    }

    internal static Border Badge(string text, bool warning)
    {
        var label = new Label { Text = text, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("Charcoal", Colors.Black) };
        return new Border
        {
            StrokeThickness = 0,
            BackgroundColor = warning ? Ui.C("SemanticWarningLight", Colors.LightYellow) : Ui.C("Gray200", Colors.LightGray),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(8, 4),
            HorizontalOptions = LayoutOptions.Start,
            Content = label
        };
    }

    /// <summary>List row for <see cref="ExpenseRow"/>; tapping runs <paramref name="open"/>.</summary>
    internal static View Row(Func<ExpenseRow, Task> open)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 12,
            Padding = new Thickness(0, 10),
            MinimumHeightRequest = 56
        };
        var icon = Ui.Decorative(new MauiIcon { IconSize = 22, IconColor = Ui.C("Gray600", Colors.Gray), VerticalOptions = LayoutOptions.Start });
        grid.Add(icon, 0);

        var texts = new VerticalStackLayout { Spacing = 2 };
        var title = new Label { FontSize = 16, TextColor = Ui.C("Charcoal", Colors.Black) };
        title.SetBinding(Label.TextProperty, nameof(ExpenseRow.CategoryLabel));
        texts.Add(title);
        var date = new Label { FontSize = 13, TextColor = Ui.C("Gray600", Colors.Gray) };
        date.SetBinding(Label.TextProperty, nameof(ExpenseRow.DateDisplay));
        texts.Add(date);
        var note = new Label { FontSize = 13, TextColor = Ui.C("Gray600", Colors.Gray), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        note.SetBinding(Label.TextProperty, nameof(ExpenseRow.NoteLine));
        note.SetBinding(VisualElement.IsVisibleProperty, nameof(ExpenseRow.HasNote));
        texts.Add(note);
        var order = Badge(string.Empty, warning: false);
        ((Label)order.Content!).SetBinding(Label.TextProperty, nameof(ExpenseRow.OrderChip));
        order.SetBinding(VisualElement.IsVisibleProperty, nameof(ExpenseRow.HasOrderChip));
        texts.Add(order);
        var pending = Badge(ExpenseRow.PendingText, warning: true);
        pending.SetBinding(VisualElement.IsVisibleProperty, nameof(ExpenseRow.ShowPending));
        texts.Add(pending);
        var voided = Badge("Voided", warning: false);
        voided.SetBinding(VisualElement.IsVisibleProperty, nameof(ExpenseRow.IsVoided));
        texts.Add(voided);
        grid.Add(texts, 1);

        var amount = new Label
        {
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Ui.C("Charcoal", Colors.Black),
            HorizontalTextAlignment = TextAlignment.End
        };
        amount.SetBinding(Label.TextProperty, nameof(ExpenseRow.AmountDisplay));
        grid.Add(amount, 2);

        grid.BindingContextChanged += (_, _) =>
        {
            if (grid.BindingContext is ExpenseRow row)
            {
                icon.Icon = Icon(row.Category);
                // Voided: struck through and grey (with the "Voided" badge, so not colour only).
                amount.TextDecorations = row.IsVoided ? TextDecorations.Strikethrough : TextDecorations.None;
                amount.TextColor = row.IsVoided ? Ui.C("Gray600", Colors.Gray) : Ui.C("Charcoal", Colors.Black);
                SemanticProperties.SetDescription(grid, row.Description);
                SemanticProperties.SetHint(grid, "Opens the expense");
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (grid.BindingContext is ExpenseRow row)
            {
                await open(row);
            }
        };
        grid.GestureRecognizers.Add(tap);
        return new VerticalStackLayout { Children = { grid, Ui.Divider() } };
    }

    internal static Label Bound(string path, double size = 14, bool bold = false, bool muted = false)
    {
        var label = new Label
        {
            FontSize = size,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            TextColor = muted ? Ui.C("Gray600", Colors.Gray) : Ui.C("Charcoal", Colors.Black),
            LineBreakMode = LineBreakMode.WordWrap
        };
        label.SetBinding(Label.TextProperty, path);
        return label;
    }

    internal static Label ErrorText(string textPath, string visiblePath)
    {
        var label = new Label { FontSize = 13, TextColor = Ui.C("SemanticOverdue", Colors.DarkRed) };
        label.SetBinding(Label.TextProperty, textPath);
        label.SetBinding(VisualElement.IsVisibleProperty, visiblePath);
        return label;
    }
}
