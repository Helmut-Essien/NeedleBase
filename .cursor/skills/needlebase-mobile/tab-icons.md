# Icons (MauiIcons.Material — no PNG UI icons)

**Do not use PNG (or SVG→MauiImage PNG) for TabBar or in-app UI icons.** NeedleBase uses **vector Material font icons** via **MauiIcons**, matching AssetTag’s in-page icon pattern (`AathifMahir.Maui.MauiIcons.Material`).

AssetTag reference: `AssetTag/MobileApp` — package `AathifMahir.Maui.MauiIcons.Material` 5.0.0, `UseMaterialMauiIcons()` in `MauiProgram.cs`, XAML `xmlns:mi`, `{mi:Material Icon=…}` on Buttons/Images, `FontImageSource` + `MaterialIcons-Regular` + `Glyph="{mi:Material …}"`, ViewModels returning `MauiIcons.Material.MaterialIcons` (e.g. `CategoryIcon`). (AssetTag’s Shell may still ship legacy `tab_*.png`; **NeedleBase must not** — use MauiIcons for tabs too.)

PNG/SVG under `Resources/Images` are only acceptable for **app launcher icon / splash**, not chrome icons.

---

## 1. Package + bootstrap

**NeedleBase.Mobile.csproj:**

```xml
<PackageReference Include="AathifMahir.Maui.MauiIcons.Material" Version="5.0.0" />
```

(Align major version with AssetTag / current stable; bump when the solution upgrades MAUI.)

**MauiProgram.cs** (after `UseMauiApp` / CommunityToolkit):

```csharp
using MauiIcons.Material;

builder
    .UseMauiApp<App>()
    .UseMauiCommunityToolkit()
    .UseMaterialMauiIcons();
```

The package registers the **MaterialIcons-Regular** font — do not hand-add a second Material font unless you have a deliberate reason.

Optional defaults (if needed):

```csharp
builder.UseMauiIconsCore(x =>
{
    x.SetDefaultIconSize(24.0);
    x.SetDefaultFontOverride(true);
});
```

---

## 2. XAML namespaces

On every page / Shell that uses icons:

```xml
xmlns:mi="http://www.aathifmahir.com/dotnet/2022/maui/icons"
```

For the **attached property** `MauiIcon.Value` (Shell tabs, bound images), also add Core (XamlC does not resolve `MauiIcon` under the `mi` xmlns alone — same pattern as AssetTag pages):

```xml
xmlns:mci="clr-namespace:MauiIcons.Core;assembly=MauiIcons.Core"
```

Use `mci:MauiIcon.Value="…"` and `{mi:Material Icon=…}` together.

---

## 3. Shell TabBar icons (required)

Use MauiIcons attached property on `<Tab>` with `TargetName='.'` so both title and icon sources get the glyph:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<Shell
    x:Class="NeedleBase.Mobile.AppShell"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:mi="http://www.aathifmahir.com/dotnet/2022/maui/icons"
    xmlns:mci="clr-namespace:MauiIcons.Core;assembly=MauiIcons.Core"
    xmlns:views="clr-namespace:NeedleBase.Mobile.Views"
    Shell.FlyoutBehavior="Disabled">

    <TabBar>
        <Tab Title="Home"
             mci:MauiIcon.Value="{mi:Material Icon=Home, TargetName='.'}">
            <ShellContent ContentTemplate="{DataTemplate views:HomePage}" Route="home" />
        </Tab>
        <Tab Title="Customers"
             mci:MauiIcon.Value="{mi:Material Icon=People, TargetName='.'}">
            <ShellContent ContentTemplate="{DataTemplate views:CustomersPage}" Route="customers" />
        </Tab>
        <Tab Title="Orders"
             mci:MauiIcon.Value="{mi:Material Icon=ReceiptLong, TargetName='.'}">
            <ShellContent ContentTemplate="{DataTemplate views:OrdersPage}" Route="orders" />
        </Tab>
        <Tab Title="Payments"
             mci:MauiIcon.Value="{mi:Material Icon=Payments, TargetName='.'}">
            <ShellContent ContentTemplate="{DataTemplate views:PaymentsPage}" Route="payments" />
        </Tab>
        <Tab Title="More"
             mci:MauiIcon.Value="{mi:Material Icon=Menu, TargetName='.'}">
            <ShellContent ContentTemplate="{DataTemplate views:MorePage}" Route="more" />
        </Tab>
    </TabBar>
</Shell>
```

**Alternative** (explicit `Icon` + FontImageSource — same font):

```xml
<Tab Title="Home">
    <Tab.Icon>
        <FontImageSource FontFamily="MaterialIcons-Regular"
                         Glyph="{mi:Material Icon=Home}"
                         Color="{StaticResource Slate}" />
    </Tab.Icon>
    ...
</Tab>
```

Prefer `mci:MauiIcon.Value` + `TargetName='.'`. On iOS, if a tab glyph is invisible, set an explicit `IconColor` / `FontImageSource.Color` (theme resource), not a DynamicResource that resolves late.

### Recommended MaterialIcons (NeedleBase tabs)

| Tab | `Icon=` enum |
|-----|----------------|
| Home | `Home` |
| Customers | `People` |
| Orders | `ReceiptLong` |
| Payments | `Payments` |
| More | `Menu` |

Pick names from `MauiIcons.Material.MaterialIcons`. Keep five tabs max.

---

## 4. In-page icons (AssetTag patterns)

### A. Button / ImageButton image

```xml
<Button Text="Sort"
        ImageSource="{mi:Material Icon=Sort, IconColor={StaticResource OnPrimary}}"
        ... />
```

### B. Image / Toolbar via FontImageSource

```xml
<Image>
    <Image.Source>
        <FontImageSource FontFamily="MaterialIcons-Regular"
                         Glyph="{mi:Material Icon=Settings}"
                         Color="{StaticResource Ink}"
                         Size="24" />
    </Image.Source>
</Image>
```

### C. Attached property on Image / ImageButton

```xml
<Image mi:MauiIcon.Value="{mi:Material Icon=Search, IconColor={StaticResource Ink}}" />
```

### D. Bound icon from ViewModel (AssetTag category / nav pattern)

```csharp
using MauiIcons.Material;

public MaterialIcons CategoryIcon => MaterialIcons.Checkroom; // or from data
```

```xml
<Image mi:MauiIcon.Value="{mi:Material Icon={Binding CategoryIcon}, IconColor={StaticResource Ink}}" />
```

Use `MaterialIcons` enum in the ViewModel — not PNG file names, not magic glyph strings in multiple places.

### E. Standalone MauiIcon control

```xml
<mi:MauiIcon Icon="{mi:Material Icon=Add}" IconSize="24" IconColor="{StaticResource Ink}" />
```

For Entry/SearchBar glyph-as-font, set `FontOverride=True` on the markup extension or via `UseMauiIconsCore`.

---

## 5. Colors

- Use design tokens (`Ink`, `Slate`, `Brand`, `OnPrimary`) — not hardcoded random hex per icon.
- Shell: set `Shell.TabBarForegroundColor` / `TabBarUnselectedColor` (or app theme) so selected/unselected tabs read clearly with font icons.
- Prefer `IconColor={StaticResource …}` over unresolved DynamicResource on iOS tab icons.

---

## 6. Do / Don’t

| Do | Don’t |
|----|--------|
| `UseMaterialMauiIcons()` + `xmlns:mi` | PNG/WebP tab assets (`tab_home.png`, etc.) |
| `{mi:Material Icon=Home}` / `MaterialIcons` enum | Hand-copied Unicode glyphs without MauiIcons |
| `mci:MauiIcon.Value` on Tab with `TargetName='.'` | SVG → MauiImage pipeline for UI icons |
| One Material family for all chrome | Mixing FontAwesome + Material + custom PNG for the same chrome |
| App icon / splash as platform images | Shipping decorative PNG “icons” for buttons and tabs |

---

## 7. Acceptance checklist

- [ ] Package `AathifMahir.Maui.MauiIcons.Material` referenced; `UseMaterialMauiIcons()` called.
- [ ] No `tab_*.png` / UI icon PNGs under `Resources/Images` for Shell or buttons.
- [ ] AppShell five tabs use `mci:MauiIcon.Value="{mi:Material Icon=…, TargetName='.'}"` (or FontImageSource + mi Glyph).
- [ ] In-page actions (sort, settings, empty-state, toolbars) use MauiIcons, not bitmaps.
- [ ] Bound icons use `MaterialIcons` in ViewModels where the glyph is data-driven.
- [ ] Icons visible on Android and iOS; tab selected/unselected colors readable.

---

## Related

- [di-navigation.md](di-navigation.md) — AppShell routes / TabBar
- [../needlebase-uiux/design-system.md](../needlebase-uiux/design-system.md) — colors for `IconColor`
- AssetTag: `MauiProgram.cs` (`UseMaterialMauiIcons`), pages using `{mi:Material …}`, VMs using `MaterialIcons`
