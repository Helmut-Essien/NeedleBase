using MauiIcons.Material;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Mobile.Helpers;

/// <summary>Status chip colours/icons — single source with content-and-states §6.</summary>
public static class OrderStatusPresentation
{
    public static string DisplayName(string status) => OrderStatus.DisplayName(status);

    public static MaterialIcons Icon(string status) => status switch
    {
        OrderStatus.New => MaterialIcons.FiberNew,
        OrderStatus.Cutting => MaterialIcons.ContentCut,
        OrderStatus.Sewing => MaterialIcons.Checkroom,
        OrderStatus.Fitting => MaterialIcons.Straighten,
        OrderStatus.Ready => MaterialIcons.CheckCircle,
        OrderStatus.Collected => MaterialIcons.Done,
        OrderStatus.Cancelled => MaterialIcons.Cancel,
        _ => MaterialIcons.Info
    };

    public static Color Background(string status) => status switch
    {
        OrderStatus.New => Resolve("Gray200", Colors.LightGray),
        OrderStatus.Cutting => Resolve("SemanticInfoLight", Color.FromArgb("#DBEAFE")),
        OrderStatus.Sewing => Resolve("PrimaryLight", Color.FromArgb("#E8F5E9")),
        OrderStatus.Fitting => Resolve("SemanticWarningLight", Color.FromArgb("#FEF3C7")),
        OrderStatus.Ready => Resolve("PrimaryDark", Color.FromArgb("#247A2B")),
        OrderStatus.Collected => Resolve("Gray200", Colors.LightGray),
        OrderStatus.Cancelled => Resolve("Gray200", Colors.LightGray),
        _ => Resolve("Gray200", Colors.LightGray)
    };

    public static Color Foreground(string status) => status switch
    {
        OrderStatus.New => Resolve("Gray600", Colors.Gray),
        OrderStatus.Cutting => Resolve("SemanticInfo", Color.FromArgb("#2563EB")),
        OrderStatus.Sewing => Resolve("PrimaryDark", Color.FromArgb("#247A2B")),
        OrderStatus.Fitting => Resolve("SemanticWarning", Color.FromArgb("#D97706")),
        OrderStatus.Ready => Resolve("White", Colors.White),
        OrderStatus.Collected => Resolve("Gray600", Colors.Gray),
        OrderStatus.Cancelled => Resolve("SemanticError", Color.FromArgb("#DC2626")),
        _ => Resolve("Gray600", Colors.Gray)
    };

    /// <summary>Strong colour for progress bars / accents (never white).</summary>
    public static Color Accent(string status) => status switch
    {
        OrderStatus.New => Resolve("Gray600", Colors.Gray),
        OrderStatus.Cutting => Resolve("SemanticInfo", Color.FromArgb("#2563EB")),
        OrderStatus.Sewing => Resolve("PrimaryDark", Color.FromArgb("#247A2B")),
        OrderStatus.Fitting => Resolve("SemanticWarning", Color.FromArgb("#D97706")),
        OrderStatus.Ready => Resolve("PrimaryDark", Color.FromArgb("#247A2B")),
        OrderStatus.Cancelled => Resolve("SemanticError", Color.FromArgb("#DC2626")),
        _ => Resolve("Gray600", Colors.Gray)
    };

    /// <summary>Derived Overdue badge (not a status): own token, never the Fitting colours.</summary>
    public static Color OverdueBackground => Resolve("SemanticErrorLight", Color.FromArgb("#FEE2E2"));

    public static Color OverdueForeground => Resolve("SemanticOverdue", Color.FromArgb("#B91C1C"));

    public static ImageSource OverdueIconImage(double size = 14) =>
        IconImage(MaterialIcons.Schedule, OverdueForeground, size);

    public static ImageSource IconImage(string status, double size = 14) =>
        IconImage(Icon(status), Foreground(status), size);

    public static ImageSource IconImage(MaterialIcons icon, Color color, double size = 14) =>
        new FontImageSource
        {
            FontFamily = "MaterialIcons-Regular",
            Glyph = char.ConvertFromUtf32(unchecked((int)icon)),
            Color = color,
            Size = size
        };

    private static Color Resolve(string key, Color fallback)
    {
        var app = Microsoft.Maui.Controls.Application.Current;
        if (app?.Resources.TryGetValue(key, out var value) == true && value is Color color)
        {
            return color;
        }

        return fallback;
    }
}
