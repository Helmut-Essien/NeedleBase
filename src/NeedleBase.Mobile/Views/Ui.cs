using MauiIcons.Core;
using MauiIcons.Material;

namespace NeedleBase.Mobile.Views;

/// <summary>Small builders for the code-only Slice 7 pages, matching the XAML design system (48dp targets, AA text).</summary>
internal static class Ui
{
    internal static Color C(string key, Color fallback) => PlaceholderChrome.ResolveColor(key, fallback);

    internal static Label Heading(string text, int level = 1)
    {
        var label = new Label
        {
            Text = text,
            FontSize = level == 1 ? 22 : 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = C("Charcoal", Colors.Black),
            Margin = new Thickness(0, level == 1 ? 8 : 12, 0, 4)
        };
        SemanticProperties.SetHeadingLevel(label, level == 1 ? SemanticHeadingLevel.Level1 : SemanticHeadingLevel.Level2);
        return label;
    }

    internal static Label Body(string? text = null, bool muted = true) => new()
    {
        Text = text,
        FontSize = 14,
        TextColor = muted ? C("Gray600", Colors.Gray) : C("Charcoal", Colors.Black),
        LineBreakMode = LineBreakMode.WordWrap
    };

    internal static Label FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontAttributes = FontAttributes.Bold,
        TextColor = C("Charcoal", Colors.Black),
        Margin = new Thickness(0, 8, 0, 4)
    };

    internal static Border Field(View input) => new()
    {
        Stroke = C("Gray300", Colors.LightGray),
        StrokeThickness = 1,
        BackgroundColor = Colors.White,
        Padding = new Thickness(14, 2),
        MinimumHeightRequest = 48,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
        Content = input
    };

    internal static Entry Entry(string placeholder, string binding, string description, Keyboard? keyboard = null, bool password = false)
    {
        var entry = new Entry
        {
            Placeholder = placeholder,
            PlaceholderColor = C("Gray600", Colors.Gray),
            TextColor = C("Charcoal", Colors.Black),
            Keyboard = keyboard ?? Keyboard.Default,
            IsPassword = password,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = !password
        };
        entry.SetBinding(Microsoft.Maui.Controls.Entry.TextProperty, binding);
        SemanticProperties.SetDescription(entry, description);
        return entry;
    }

    internal static Button Primary(string text, string command)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = C("PrimaryDark", Colors.DarkGreen),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 16,
            CornerRadius = 10,
            HeightRequest = 48,
            Margin = new Thickness(0, 12, 0, 0)
        };
        button.SetBinding(Button.CommandProperty, command);
        return button;
    }

    internal static Button Secondary(string text)
    {
        var button = new Button { Text = text, CornerRadius = 10, HeightRequest = 48, FontAttributes = FontAttributes.Bold, FontSize = 15 };
        if (Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue("SecondaryButton", out var style) == true && style is Style s)
        {
            button.Style = s;
        }

        return button;
    }

    internal static Border Banner(string textBinding, string visibleBinding, bool error)
    {
        var label = new Label { FontSize = 14, TextColor = error ? C("SemanticOverdue", Colors.DarkRed) : C("PrimaryDark", Colors.DarkGreen) };
        label.SetBinding(Label.TextProperty, textBinding);
        var border = new Border
        {
            BackgroundColor = error ? C("SemanticErrorLight", Colors.MistyRose) : C("PrimaryLight", Colors.Honeydew),
            StrokeThickness = 0,
            Padding = new Thickness(12, 10),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Content = label
        };
        border.SetBinding(VisualElement.IsVisibleProperty, visibleBinding);
        // No description on the box: on Android it would replace the message, so TalkBack read only "Error".
        // New messages are announced, since they appear away from where the person is focused (the button).
        AutomationProperties.SetIsInAccessibleTree(label, true);
        label.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == Label.TextProperty.PropertyName && !string.IsNullOrWhiteSpace(label.Text))
            {
                try
                {
                    SemanticScreenReader.Default.Announce(error ? "Error. " + label.Text : label.Text);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Announce failed: {ex.Message}");
                }
            }
        };
        return border;
    }

    internal static Border Card(View content) => new()
    {
        BackgroundColor = Colors.White,
        Stroke = C("Gray300", Colors.LightGray),
        StrokeThickness = 1,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
        Padding = new Thickness(16, 12),
        Content = content
    };

    internal static View LinkRow(string text, MaterialIcons icon, Func<Task> onTap, string automationId, string? subtitle = null)
    {
        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 12,
            MinimumHeightRequest = 48,
            AutomationId = automationId
        };
        row.Add(new MauiIcon { Icon = icon, IconSize = 22, IconColor = C("Gray600", Colors.Gray), VerticalOptions = LayoutOptions.Center }, 0);
        var texts = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center };
        texts.Add(new Label { Text = text, FontSize = 16, TextColor = C("Charcoal", Colors.Black) });
        if (subtitle is not null)
        {
            texts.Add(new Label { Text = subtitle, FontSize = 13, TextColor = C("Gray600", Colors.Gray) });
        }

        row.Add(texts, 1);
        row.Add(new MauiIcon { Icon = MaterialIcons.ChevronRight, IconSize = 22, IconColor = C("Gray600", Colors.Gray), VerticalOptions = LayoutOptions.Center }, 2);
        SemanticProperties.SetDescription(row, subtitle is null ? $"Open {text}" : $"Open {text}. {subtitle}");
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await onTap();
        row.GestureRecognizers.Add(tap);
        return row;
    }

    internal static BoxView Divider() => new() { HeightRequest = 1, Color = C("Gray200", Colors.LightGray) };

    internal static ContentPage Chrome(ContentPage page, string title, View body, bool backButton)
    {
        page.Title = title;
        Shell.SetNavBarIsVisible(page, false);
        NavigationPage.SetHasNavigationBar(page, false);
        page.BackgroundColor = C("AppBackground", Colors.WhiteSmoke);
        var stack = new VerticalStackLayout { Padding = new Thickness(16, 8, 16, 32), Spacing = 8 };
        if (backButton)
        {
            var back = new Button
            {
                Text = "‹ Back",
                BackgroundColor = Colors.Transparent,
                TextColor = C("PrimaryDark", Colors.DarkGreen),
                FontSize = 16,
                HeightRequest = 48,
                Padding = new Thickness(0),
                HorizontalOptions = LayoutOptions.Start
            };
            SemanticProperties.SetDescription(back, "Go back");
            back.Clicked += async (_, _) => await Shell.Current.GoToAsync("..");
            stack.Add(back);
        }

        stack.Add(body);
        page.Content = new ScrollView { Content = stack };
        return page;
    }
}
