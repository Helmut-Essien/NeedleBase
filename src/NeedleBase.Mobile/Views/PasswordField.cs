using MauiIcons.Core;
using MauiIcons.Material;

namespace NeedleBase.Mobile.Views;

/// <summary>
/// Password box with the same show/hide eye as the sign-in page: closed eye while hidden, open eye while shown,
/// a 44dp touch target, and a spoken "Show password" / "Hide password" description. Used by the code-only pages
/// (change password, forgot/reset password, accept invite); the XAML sign-in and register pages carry the same markup.
/// </summary>
public sealed class PasswordField : Border
{
    private readonly Button _toggle;
    private readonly MauiIcon _eyeClosed;
    private readonly MauiIcon _eyeOpen;
    private readonly string _label;
    private bool _isVisible;

    public PasswordField(string placeholder, string textBinding, string description, string automationId)
    {
        _label = description;
        Stroke = Ui.C("Gray300", Colors.LightGray);
        StrokeThickness = 1;
        BackgroundColor = Colors.White;
        Padding = new Thickness(14, 2, 4, 2);
        MinimumHeightRequest = 48;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 };

        Input = Ui.Entry(placeholder, textBinding, description, password: true);
        Input.AutomationId = automationId;

        var iconColor = Ui.C("Gray600", Colors.Gray);
        _eyeClosed = new MauiIcon { Icon = MaterialIcons.VisibilityOff, IconSize = 21, IconColor = iconColor, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _eyeOpen = new MauiIcon { Icon = MaterialIcons.Visibility, IconSize = 21, IconColor = iconColor, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, IsVisible = false };
        AutomationProperties.SetIsInAccessibleTree(_eyeClosed, false);
        AutomationProperties.SetIsInAccessibleTree(_eyeOpen, false);

        _toggle = new Button
        {
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            WidthRequest = 44,
            HeightRequest = 44,
            MinimumWidthRequest = 44,
            MinimumHeightRequest = 44,
            Padding = 0,
            CornerRadius = 22,
            AutomationId = automationId + "Toggle"
        };
        _toggle.Clicked += (_, _) => Toggle();

        var toggleHost = new Grid { WidthRequest = 44, HeightRequest = 44, VerticalOptions = LayoutOptions.Center };
        toggleHost.Add(_eyeClosed);
        toggleHost.Add(_eyeOpen);
        toggleHost.Add(_toggle);

        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(Input, 0);
        row.Add(toggleHost, 1);
        Content = row;
        Apply();
    }

    public Entry Input { get; }

    public bool IsPasswordVisible => _isVisible;

    public string ToggleDescription => _isVisible ? "Hide password" : "Show password";

    public void Toggle()
    {
        _isVisible = !_isVisible;
        Apply();
        try
        {
            SemanticScreenReader.Default.Announce(ToggleDescription);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    /// <summary>Hide the text again (used when the page goes away so a password is never left showing).</summary>
    public void Remask()
    {
        if (_isVisible)
        {
            _isVisible = false;
            Apply();
        }
    }

    private void Apply()
    {
        Input.IsPassword = !_isVisible;
        _eyeClosed.IsVisible = !_isVisible;
        _eyeOpen.IsVisible = _isVisible;
        SemanticProperties.SetDescription(_toggle, ToggleDescription);
    }
}
