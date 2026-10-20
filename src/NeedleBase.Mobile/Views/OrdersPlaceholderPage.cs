using MauiIcons.Core;
using MauiIcons.Material;

namespace NeedleBase.Mobile.Views;

/// <summary>Shared placeholder chrome for unbuilt tabs (Payments/More). Orders uses <see cref="OrdersPage"/>.</summary>
internal static class PlaceholderChrome
{
    internal static View Build(string title, string message, MaterialIcons icon)
    {
        var iconView = new MauiIcon
        {
            Icon = icon,
            IconSize = 48,
            IconColor = ResolveColor("Gray600", Colors.Gray),
            HorizontalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(iconView, $"{title} placeholder");

        var titleLabel = new Label
        {
            Text = title,
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = ResolveColor("Charcoal", Colors.Black),
            HorizontalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetHeadingLevel(titleLabel, SemanticHeadingLevel.Level1);

        return new VerticalStackLayout
        {
            Padding = new Thickness(24, 40, 24, 24),
            Spacing = 12,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                iconView,
                titleLabel,
                new Label
                {
                    Text = message,
                    FontSize = 15,
                    TextColor = ResolveColor("Gray600", Colors.Gray),
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };
    }

    internal static Color ResolveColor(string key, Color fallback)
    {
        var app = Microsoft.Maui.Controls.Application.Current;
        if (app?.Resources.TryGetValue(key, out var value) == true && value is Color color)
        {
            return color;
        }

        return fallback;
    }
}

public sealed class PaymentsPlaceholderPage : ContentPage
{
    public PaymentsPlaceholderPage()
    {
        Title = "Payments";
        Shell.SetNavBarIsVisible(this, false);
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = PlaceholderChrome.ResolveColor("AppBackground", Colors.WhiteSmoke);
        Content = PlaceholderChrome.Build(
            "Payments",
            "Payments and balances arrive in a later slice.",
            MaterialIcons.Payments);
    }
}

public sealed class MorePlaceholderPage : ContentPage
{
    public MorePlaceholderPage()
    {
        Title = "More";
        Shell.SetNavBarIsVisible(this, false);
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = PlaceholderChrome.ResolveColor("AppBackground", Colors.WhiteSmoke);

        var intro = PlaceholderChrome.Build(
            "More",
            "Settings, sync details, and staff land in later slices.",
            MaterialIcons.Menu);

        var aboutHeading = new Label
        {
            Text = "About",
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = PlaceholderChrome.ResolveColor("Charcoal", Colors.Black)
        };
        SemanticProperties.SetHeadingLevel(aboutHeading, SemanticHeadingLevel.Level2);

        var version = new Label
        {
            Text = $"NeedleBase version {AppInfo.Current.VersionString}",
            FontSize = 14,
            TextColor = PlaceholderChrome.ResolveColor("Gray600", Colors.Gray)
        };

        var about = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = PlaceholderChrome.ResolveColor("Gray300", Colors.LightGray),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(16, 8),
            Content = new VerticalStackLayout
            {
                Children =
                {
                    BuildLinkRow("Terms and Conditions", MaterialIcons.Description, nameof(TermsPage), "MoreTermsLink"),
                    new BoxView { HeightRequest = 1, Color = PlaceholderChrome.ResolveColor("Gray200", Colors.LightGray) },
                    BuildLinkRow("Privacy Policy", MaterialIcons.PrivacyTip, nameof(PrivacyPage), "MorePrivacyLink")
                }
            }
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 0, 16, 24),
                Spacing = 12,
                Children = { intro, aboutHeading, about, version }
            }
        };
    }

    private static View BuildLinkRow(string text, MaterialIcons icon, string route, string automationId)
    {
        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 12,
            MinimumHeightRequest = 48,
            AutomationId = automationId
        };
        row.Add(new MauiIcon { Icon = icon, IconSize = 22, IconColor = PlaceholderChrome.ResolveColor("Gray600", Colors.Gray), VerticalOptions = LayoutOptions.Center }, 0);
        row.Add(new Label { Text = text, FontSize = 16, TextColor = PlaceholderChrome.ResolveColor("Charcoal", Colors.Black), VerticalOptions = LayoutOptions.Center }, 1);
        row.Add(new MauiIcon { Icon = MaterialIcons.ChevronRight, IconSize = 22, IconColor = PlaceholderChrome.ResolveColor("Gray600", Colors.Gray), VerticalOptions = LayoutOptions.Center }, 2);
        SemanticProperties.SetDescription(row, $"Open {text}");

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Shell.Current.GoToAsync(route);
        row.GestureRecognizers.Add(tap);
        return row;
    }
}
