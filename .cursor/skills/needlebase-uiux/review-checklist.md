# Pre-merge UI review checklist

Run on every UI PR before asking for review. Evidence = a build with 0 errors/0 warnings + a device/emulator screenshot (375-wide) for each touched screen.

## Tokens & visuals
- [ ] Only keys from Colors.xaml / `--nb-*` vars ([design-system.md](design-system.md) token map); no new raw hex
- [ ] Radii: inputs 8, buttons 10, cards 12; buttons 48 high; page padding 16
- [ ] Icons are MauiIcons, explicitly sized ([tab-icons.md](../needlebase-mobile/tab-icons.md))

## Contrast & accessibility
- [ ] Text links `PrimaryDark`, not `Primary`; no text in `Gray500`/`Gray300`
- [ ] Disabled + placeholder text `Gray600`
- [ ] Filled buttons use `PrimaryDark` (pressed `PrimaryPressed`), never `Primary`
- [ ] No entry points to unbuilt features (Notifications, Forgot password)
- [ ] Icon-only controls: 44×44 target, `SemanticProperties.Description`/`aria-label`, `AutomationId`
- [ ] One heading per screen; hierarchy logo → h1 → section titles (no duplicate "Sign in")
- [ ] Status = colour + text + icon

## Forms
- [ ] [validation.md](validation.md) pattern; Terms/consent gates inline
- [ ] Password fields use the toggle pattern ([maui-patterns.md](maui-patterns.md#password-visibility-toggle))
- [ ] Long labels wrap (Grid `*` column, no HorizontalStackLayout)
- [ ] Inputs readable on a dark-mode phone (no `AppThemeBinding Dark=White` text on light surfaces; see [maui-patterns.md](maui-patterns.md#inputs-are-light-only-dark-mode-phones))
- [ ] Search-as-you-type uses `SearchDebouncer` + the shared customer rule (name word prefix, phone digits), capped results, empty state with a next action

## Navigation
- [ ] No Shell nav bar; pushed pages have in-page Back
- [ ] Android back: pops, then double-back-to-exit on tab roots (don't override per page)

## Data honesty
- [ ] No hard-coded/fake numbers on real screens — zero/empty states when there is no data (Home uses real `GetCachedUserAsync` + honest zeros)
- [ ] Offline copy: "Saved on this device", never "synced"
- [ ] Empty, loading, error states present

## Content & states
- [ ] Money/date/phone/status names per [content-and-states.md](content-and-states.md); unbuilt features not shown as live (no dead links, e.g. Forgot password)
- [ ] Loading/empty/error/offline defined for the screen type (§4); API `message` mapped, never raw exception text

## Design source
- [ ] Matches Figma file `8qquGru92p56agRvfSRBie` (pages *Auth — Login & Register*, *Mobile — Home*) where a frame exists; deviations noted in the PR
- [ ] Where Figma conflicts with [mobile-screens.md](mobile-screens.md) (e.g. Home KPI cards vs "avoid vanity KPIs"), flag it — don't silently pick one

## Speed & local data (mobile)
- [ ] Lists follow [lists-and-local-data.md](../needlebase-mobile/lists-and-local-data.md): cached rows, spinner only when empty, new write paths bump `LocalDataVersion`
- [ ] Visible lists refresh live via `LiveListRefresh` (attach on appear, detach on disappear and session clear); quiet reload, rows updated with `CollectionSync`
- [ ] Rows: shared icon images, precomputed strings, no `MultiBinding`; queries off the UI thread
- [ ] Icons via `MaterialIcons` alias and `Glyph()`; new status/icon added to `StatusIconGlyphTests`
- [ ] Speed change measured on the phone (NB_PERF before/after)

## Safety
- [ ] `.vscode` launch/tasks untouched
- [ ] Edit/wizard VMs with a save implement `IBackAware` (Android Back can't pop mid-save)
- [ ] Money shown via en-GH formatted properties, not XAML `StringFormat`
- [ ] Phone fields: `PhoneNumbers.Check` on new/changed numbers; duplicate number warns, never blocks


## Web extras
- [ ] No inline script/style (CSP); assets have `asp-append-version` and `defer`.
- [ ] POST buttons have `data-pending-text`; double-click submits once.
- [ ] Text colours meet 4.5:1 (no Primary `#2E9E36` or old `#8A949A` as text on white).
- [ ] Empty pages show one message, no empty scaffolding; counts pluralised.

## Web v4 (soft UI + drawer)

- [ ] Uses v4 tokens/components from `site.css`; no inline styles/scripts; raised elements carry the `edge` hairline
- [ ] Text contrast ≥4.5:1 on `#EEF2EF` (warning `#9A4406`, never `#B45309`; white only on `#247A2B`+)
- [ ] <768: hamburger opens drawer; × / scrim / Escape / link close it; focus trapped and returned; `aria-expanded` toggles; works without JS (`#nb-drawer` :target)
- [ ] No horizontal scroll at 390, 768, 1440; tables become cards <768
- [ ] No CSP console violations (check DevTools console on every page)

- [ ] Forced-colours (DevTools ▸ Rendering ▸ forced-colors) shows borders on every card/well; print preview is clean
- [ ] Long names and GH₵ 12,345,678.90 don't overflow at 320
