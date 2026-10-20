# Icons: MauiIcons.Material (single source)

Every UI icon (Shell tabs, buttons, in-field toggles, list glyphs, empty states) is a **Material font icon** via `AathifMahir.Maui.MauiIcons.Material`. PNG/SVG under `Resources/Images` are only for the launcher icon and splash. Never use `tab_*.png`, never `dotnet_bot.png` on tabs, never mix icon families.

## Bootstrap (current)

```xml
<PackageReference Include="AathifMahir.Maui.MauiIcons.Material" Version="5.0.0" />
```
```csharp
builder.UseMauiApp<App>().UseMauiCommunityToolkit().UseMaterialMauiIcons();
```
The package registers the `MaterialIcons-Regular` font; don't add another.

XAML namespaces (both are needed for the attached property; XamlC doesn't resolve `MauiIcon` via `mi` alone):
```xml
xmlns:mi="http://www.aathifmahir.com/dotnet/2022/maui/icons"
xmlns:mci="clr-namespace:MauiIcons.Core;assembly=MauiIcons.Core"
```

## Shell tabs (current `AppShell.xaml`)

```xml
<TabBar Route="main">
    <Tab Title="Home" Route="HomePage"
         mci:MauiIcon.Value="{mi:Material Icon=Home, IconSize=22, TargetName='.'}">
        <ShellContent ContentTemplate="{DataTemplate views:HomePage}" />
    </Tab>
    <!-- Customers=People, Orders=ReceiptLong, Payments=Payments, More=Menu; same pattern, IconSize=22 -->
</TabBar>
```

Tab colours live on the TabBar (`TabBarBackgroundColor="White"`, Foreground/Title `Primary`, Unselected `Gray500`). Details: [maui-patterns.md](../needlebase-uiux/maui-patterns.md#shell-tabbar-styling). Max five tabs.

## In-page patterns

```xml
<!-- A. Image via FontImageSource (preferred for sized glyphs) -->
<Image WidthRequest="24" HeightRequest="24">
    <Image.Source>
        <FontImageSource FontFamily="MaterialIcons-Regular" Glyph="{mi:Material Icon=Settings}"
                         Color="{StaticResource Charcoal}" Size="24" />
    </Image.Source>
</Image>

<!-- B. Filled primary button with icon (fill comes from the global Button style = PrimaryDark; CustomersPage "Add") -->
<Button Text="Add" ImageSource="{mi:Material Icon=PersonAdd, IconColor=White}" HeightRequest="48" />

<!-- C. Standalone control -->
<mi:MauiIcon Icon="{mi:Material Icon=Add}" IconSize="24" IconColor="{StaticResource Charcoal}" />
```

C#: `new MauiIcon { Icon = MaterialIcons.ChevronRight, IconSize = 22, IconColor = … }` (used in `MorePlaceholderPage`).

Data-driven glyphs: expose `MauiIcons.Material.MaterialIcons` from the VM (enum, never magic strings) and bind `{mi:Material Icon={Binding CategoryIcon}, IconColor=…}`.

## Rules

1. **Always size:** `IconSize`/`Size` **and** host `WidthRequest/HeightRequest`. Unsized icons fill their container. Tabs 22 · in-field toggles 21 (in a 21×21 Image) · actions 24 · empty states 48.
2. **Colours from real keys:** `Charcoal`, `Gray600` (default glyph), `Gray500` (unselected tab), `Primary` (selected/accent), `White` on filled buttons. No raw hex; no invented keys. Token map: [design-system.md](../needlebase-uiux/design-system.md#token--code-map-source-of-truth-is-code-keep-this-table-in-sync).
3. **Don't swap glyphs by DataTrigger on `ImageButton.Source`.** It didn't re-render on Android. Use two Images toggled by `IsVisible` ([password toggle](../needlebase-uiux/maui-patterns.md#password-visibility-toggle)).
4. Icon-only controls: 44×44 target + `SemanticProperties.Description` + `AutomationId`.
5. Check the enum exists in `MaterialIcons` before using a name.

## Acceptance

- [ ] No UI icon PNGs in `Resources/Images`; tabs use `mci:MauiIcon.Value` with `IconSize=22, TargetName='.'`
- [ ] Every icon sized; colours from Colors.xaml keys
- [ ] Selected/unselected tabs readable on device
