using MauiIcons.Material;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Mobile.Helpers;

/// <summary>Status chip colours/icons — single source with content-and-states §6.</summary>
public static class OrderStatusPresentation
{
    /// <summary>Statuses this app version doesn't know (added later on the server) show a neutral label, not raw text.</summary>
    public const string UnknownStatusLabel = "Other status";

    public static string DisplayName(string status) =>
        OrderStatus.IsValid(status) ? OrderStatus.DisplayName(status) : UnknownStatusLabel;

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

    // List rows share one icon per status (built on the UI thread) instead of new image sources per row.
    private static readonly Dictionary<string, ImageSource> RowIcons = new(StringComparer.Ordinal);
    private static ImageSource? _rowOverdueIcon;

    public static ImageSource RowIconImage(string status)
    {
        lock (RowIcons)
        {
            if (!RowIcons.TryGetValue(status, out var icon))
            {
                icon = IconImage(status);
                RowIcons[status] = icon;
            }

            return icon;
        }
    }

    public static ImageSource RowOverdueIconImage() => _rowOverdueIcon ??= OverdueIconImage(14);

    /// <summary>
    /// Font alias MauiIcons registers in UseMaterialMauiIcons(). The XAML <c>{mi:Material}</c> extension sets it
    /// itself, so "MaterialIcons-Regular" only appeared to work there; in code it fell back to a system font. XAML uses this alias too.
    /// </summary>
    public const string MaterialFontFamily = "MaterialIcons";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<MaterialIcons, string> Glyphs = new();

    /// <summary>
    /// The font glyph for a MauiIcons enum member. The enum's numeric value is only an index; the real
    /// codepoint is in its [Description] (what the XAML <c>{mi:Material}</c> extension uses). Casting the enum to
    /// int drew unrelated characters (e.g. U+076E, an Arabic letter, for Straighten/Fitting).
    /// </summary>
    public static string Glyph(MaterialIcons icon) => Glyphs.GetOrAdd(icon, static i =>
        typeof(MaterialIcons).GetField(i.ToString())?
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .OfType<System.ComponentModel.DescriptionAttribute>()
            .FirstOrDefault()?.Description ?? string.Empty);

    public static ImageSource IconImage(MaterialIcons icon, Color color, double size = 14) =>
        new FontImageSource
        {
            FontFamily = MaterialFontFamily,
            Glyph = Glyph(icon),
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
