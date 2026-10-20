# MAUI UI patterns & gotchas (learned in NeedleBase)

Proven patterns from the shipped mobile app. Copy these instead of reinventing. Icons: [tab-icons.md](../needlebase-mobile/tab-icons.md). MVVM: [needlebase-mobile](../needlebase-mobile/SKILL.md).

---

## Password visibility toggle

Source: `Views/LoginPage.xaml`, `Views/RegisterPage.xaml`. Two pre-sized Images toggled by `IsVisible`, with a transparent 44px Button on top for the tap target and screen-reader label.

```xml
<Grid WidthRequest="44" HeightRequest="44">
    <Image WidthRequest="21" HeightRequest="21" IsVisible="{Binding IsPasswordHidden}">
        <Image.Source>
            <FontImageSource FontFamily="MaterialIcons-Regular"
                             Glyph="{mi:Material Icon=VisibilityOff}"
                             Size="21" Color="{StaticResource Gray600}" />
        </Image.Source>
    </Image>
    <Image WidthRequest="21" HeightRequest="21" IsVisible="{Binding IsPasswordVisible}">
        <!-- same FontImageSource with Icon=Visibility -->
    </Image>
    <Button BackgroundColor="Transparent" WidthRequest="44" HeightRequest="44"
            Command="{Binding TogglePasswordVisibilityCommand}"
            SemanticProperties.Description="{Binding PasswordToggleDescription}"
            AutomationId="LoginPasswordToggle" />
</Grid>
```

Why: a DataTrigger swapping `ImageButton.Source` did not re-render, and an unsized icon blew up to fill the field. Web equivalent: `Pages/Shared/_PasswordToggleButton.cshtml` + `wwwroot/js/password-toggle.js` (20px SVG, `aria-label`). AutomationIds in use: `LoginPasswordToggle`, `RegisterPasswordToggle`, `RegisterConfirmPasswordToggle`.

## Checkbox + wrapping text (Terms row)

Source: `Views/RegisterPage.xaml`. **Never** put wrapping text in a `HorizontalStackLayout` — it measures with infinite width and won't wrap.

```xml
<Grid ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto" ColumnSpacing="8">
    <CheckBox VerticalOptions="Start" ... />
    <Label Grid.Column="1" LineBreakMode="WordWrap">
        <Label.FormattedText>
            <FormattedString>
                <Span Text="I agree to the " />
                <Span Text="Terms" TextColor="{StaticResource PrimaryDark}"
                      FontAttributes="Bold" TextDecorations="Underline">
                    <Span.GestureRecognizers>
                        <TapGestureRecognizer Tapped="OnTermsClicked" />
                    </Span.GestureRecognizers>
                </Span>
                <!-- … Privacy span → OnPrivacyClicked -->
            </FormattedString>
        </Label.FormattedText>
    </Label>
    <Label Grid.Row="1" Grid.Column="1" ... TermsError />
</Grid>
```

Links use `PrimaryDark` (5.39:1), not `Primary` (3.47:1).

## Android back: double-press to exit

Source: `AppShell.xaml.cs` `OnBackButtonPressed`. Order: pop modal → pop navigation stack → first press on a root shows `Toast.Make("Press back again to exit", ToastDuration.Short, 14)` → second press within 2s calls `Microsoft.Maui.Controls.Application.Current?.Quit()`. Reset the timestamp on `Navigated`. Don't re-implement per page.

## Shell TabBar styling

`AppShell.xaml`: `Shell.TabBarBackgroundColor="White"`, Foreground/Title = `Primary`, Unselected = `Gray500` (mirrored in the global Shell style in `Styles.xaml`). Tab icons `{mi:Material Icon=…, IconSize=22, TargetName='.'}`. There is **no** `Shell.TabBarTitleFontSize`. Nav bar hidden app-wide — see needlebase-mobile hard rule 14.

## Layout

- Page padding **16** on every page (Terms, Privacy and edit pages were normalised to this).
- Inputs: `Border` with `StrokeShape="RoundRectangle 8"`, stroke `Gray300`, Error stroke via DataTrigger → `SemanticError`.
- Buttons 48 high, `CornerRadius="10"`; cards radius 12. Fill comes from the global Button style (`PrimaryDark`, Pressed → `PrimaryPressed`). Don't set `BackgroundColor="{StaticResource Primary}"` on buttons; override only for secondary/ghost buttons (White/Transparent).
- Text that may be long: `LineBreakMode="WordWrap"` inside a `*` column/`VerticalStackLayout`, never in a horizontal stack.

## Gotchas (compile/runtime-verified)

| Don't | Do |
|-------|-----|
| `Entry CursorColor` | Not in MAUI 9 — omit |
| `Label LineSpacing` | `LineHeight` |
| `Shell.TabBarTitleFontSize` | Doesn't exist |
| `Application.Current` inside `NeedleBase.Mobile.*` | `Microsoft.Maui.Controls.Application` (namespace collision) |
| DataTrigger swapping `ImageButton.Source` | Two Images + `IsVisible` |
| Unsized MauiIcons | Always set `Size`/`IconSize` + host Width/Height |
| Disabled/placeholder text `Gray300`/`Gray500` | `Gray600` (Styles.xaml) |
| Same attribute twice on an element | XAML compile error — check after merges |
