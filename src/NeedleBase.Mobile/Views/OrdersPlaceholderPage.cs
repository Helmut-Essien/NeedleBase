using MauiIcons.Core;
using MauiIcons.Material;

namespace NeedleBase.Mobile.Views;

public sealed class OrdersPlaceholderPage : ContentPage
{
    public OrdersPlaceholderPage()
    {
        Title = "Orders";
        BackgroundColor = ResolveColor("AppBackground", Colors.WhiteSmoke);
        Content = Build("Orders", "Order creation arrives in the next slice.", MaterialIcons.ReceiptLong);
    }

    internal static View Build(string title, string message, MaterialIcons icon)
    {
        var iconView = new MauiIcon
        {
            Icon = icon,
            IconSize = 48,
            IconColor = ResolveColor("Gray500", Colors.Gray),
            HorizontalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(iconView, $"{title} placeholder");

        return new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 12,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                iconView,
                new Label
                {
                    Text = title,
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = ResolveColor("Charcoal", Colors.Black),
                    HorizontalOptions = LayoutOptions.Center
                },
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
        BackgroundColor = OrdersPlaceholderPage.ResolveColor("AppBackground", Colors.WhiteSmoke);
        Content = OrdersPlaceholderPage.Build(
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
        BackgroundColor = OrdersPlaceholderPage.ResolveColor("AppBackground", Colors.WhiteSmoke);
        Content = OrdersPlaceholderPage.Build(
            "More",
            "Settings, sync details, and staff land in later slices.",
            MaterialIcons.Menu);
    }
}
