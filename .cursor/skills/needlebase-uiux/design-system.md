# NeedleBase Design System

Parent brand: HelmutCode. Product: NeedleBase. Use supplied logo unchanged.

## Colour tokens

### Brand

| Token | Hex | Use |
|-------|-----|-----|
| `color.brand.primary` | `#2E9E36` | Primary buttons, active nav, positive states, key highlights |
| `color.brand.primaryDark` | `#247A2B` | Hover / pressed |
| `color.brand.primaryLight` | `#E8F5E9` | Soft backgrounds, selected rows/chips |
| `color.brand.charcoal` | `#202A32` | Headings, primary text, strong nav |
| `color.brand.charcoalSoft` | `#2D3A43` | Secondary dark elements |

### Neutrals & surfaces

| Token | Hex | Use |
|-------|-----|-----|
| `color.bg.app` | `#F4F6F5` | App / page background |
| `color.bg.surface` | `#FFFFFF` | Cards, sheets, panels |
| `color.border.default` | `#DDE3DF` | Dividers, card borders, inputs |
| `color.border.strong` | `#C5CDC8` | Emphasised borders |
| `color.text.primary` | `#202A32` | Body / titles |
| `color.text.secondary` | `#5B656B` | Supporting copy |
| `color.text.tertiary` | `#8A949A` | Placeholders, meta |
| `color.text.onPrimary` | `#FFFFFF` | Text on green buttons |

### Semantic

| Token | Hex | Use |
|-------|-----|-----|
| `color.semantic.success` | `#2E9E36` | Success (same as primary) |
| `color.semantic.warning` | `#D97706` | Warning, Fitting stage |
| `color.semantic.error` | `#DC2626` | Errors, destructive |
| `color.semantic.info` | `#2563EB` | Informational |
| `color.semantic.errorLight` | `#FEE2E2` | Error/overdue tint background (`SemanticErrorLight`) |
| `color.semantic.overdue` | `#B91C1C` | Overdue badge text/icon (`SemanticOverdue`) |

### States

| Token | Value | Use |
|-------|-------|-----|
| `color.focus.ring` | `#2E9E36` | Focus outline |
| `color.disabled.bg` | `#E8ECEA` | Disabled controls |
| `color.disabled.text` | `#5B656B` | Disabled labels (was `#A0A8AD` — 2.4:1 on white, unreadable; code uses `Gray600`) |
| `color.overlay` | `rgba(32, 42, 50, 0.5)` | Modal scrim |

### Token → code map (source of truth is code; keep this table in sync)

| Token | Mobile `Resources/Styles/Colors.xaml` | Web `wwwroot/css/site.css` |
|-------|----------------------------------------|----------------------------|
| brand.primary `#2E9E36` | `Primary` | `--nb-brand-primary` |
| brand.primaryDark `#247A2B` | `PrimaryDark` | `--nb-brand-primary-dark` |
| brand.primaryPressed `#1D6B23` | `PrimaryPressed` | `--nb-brand-primary-pressed` |
| brand.primaryLight `#E8F5E9` | `PrimaryLight` | `--nb-brand-primary-light` |
| charcoal / text.primary `#202A32` | `Charcoal` (= `Gray950`) | `--nb-charcoal` |
| charcoalSoft `#2D3A43` | `Gray900` | — |
| bg.app `#F4F6F5` | `AppBackground` (= `Gray100`) | `--nb-bg-app` |
| disabled.bg `#E8ECEA` | `Gray200` | — |
| border.default `#DDE3DF` | `Gray300` | `--nb-border` |
| border.strong `#C5CDC8` | `Gray400` | — |
| text.tertiary `#8A949A` | `Gray500` | `--nb-text-tertiary` |
| text.secondary `#5B656B` | `Gray600` | `--nb-text-secondary` |
| semantic.warning / error / info | `SemanticWarning` / `SemanticError` / `SemanticInfo` | `--nb-warning` / `--nb-error` / — |

Never invent keys (`Ink`, `Slate`, `Brand` do **not** exist). Add a token in Colors.xaml/site.css first, then here.

### Contrast (measured, WCAG 2.x)

| Pair | Ratio | Verdict |
|------|-------|---------|
| `#202A32` / `#5B656B` on white | ≥5.9:1 | Body, secondary, placeholder, disabled text ✅ |
| `#247A2B` (PrimaryDark) on white | 5.39:1 | **Use for text links** ✅ |
| `#DC2626` on white | 4.83:1 | Error text ✅ |
| White on `#247A2B` (filled button) | 5.39:1 | **Primary button fill** ✅ |
| White on `#1D6B23` (pressed/hover) | 6.6:1 | Pressed/hover fill ✅ |
| `#2E9E36` on white / white on `#2E9E36` | 3.47:1 | Icons, selected tab, checkbox, accents, borders only — **not** text, links or button fills |
| `#8A949A` (Gray500) on white | 3.1:1 | Non-text UI only (unselected tab icon, meta ≥18px); never body text or placeholder. Web uses `#69737A` (4.7:1) for `--nb-text-tertiary` because it carries meta/footer text |
| `#DDE3DF` on white | 1.3:1 | Decorative borders only — never text, never sole state signal |

Decision (resolved): filled primary buttons use `PrimaryDark`, with pressed/hover `PrimaryPressed`. Mobile: global `Button` style in Styles.xaml (Pressed VisualState); buttons must **not** set `BackgroundColor="{StaticResource Primary}"`. Web: `.nb-btn-primary` / `.btn-primary` in site.css.

**Rules:** Green = primary actions + positive feedback. Charcoal = hierarchy. White cards on `#F4F6F5`. Always pair colour with text or icons. Maintain WCAG AA.

---

## Typography

**Family:** system UI sans on web (no web-font download: patchy networks, CSP same-origin). Mobile: Inter/OpenSans as bundled.

| Token | Size / line | Weight | Use |
|-------|-------------|--------|-----|
| `type.display` | 28 / 34 | 700 | Rare hero (Welcome) |
| `type.h1` | 24 / 32 | 700 | Page titles (web) |
| `type.h2` | 20 / 28 | 600 | Section titles |
| `type.h3` | 17 / 24 | 600 | Card titles, mobile titles |
| `type.body` | 15 / 22 | 400 | Default body (mobile 16 / 24 ok) |
| `type.bodyStrong` | 15 / 22 | 600 | Emphasised body |
| `type.caption` | 13 / 18 | 400 | Meta, timestamps |
| `type.overline` | 11 / 14 | 600 | Uppercase labels (use sparingly) |
| `type.numeric` | tabular nums | 500–600 | Measurements, money |

Currency format (GHS): `GHS 150.00` or `GH₵150.00` — pick one and stay consistent (prefer `GH₵` in UI, `GHS` in receipts if needed).

---

## Spacing & layout

**Base unit:** 4px.

| Token | Value |
|-------|-------|
| `space.1` | 4 |
| `space.2` | 8 |
| `space.3` | 12 |
| `space.4` | 16 |
| `space.5` | 20 |
| `space.6` | 24 |
| `space.8` | 32 |
| `space.10` | 40 |
| `space.12` | 48 |

| Context | Padding |
|---------|---------|
| Mobile screen horizontal | 16 |
| Mobile card padding | 16 |
| Web content max width | 1200–1440 content area |
| Web page padding | 24–32 |
| Form field gap | 12–16 |
| Section gap | 24 |

---

## Radii & elevation

| Token | Value | Use |
|-------|-------|-----|
| `radius.sm` | 8 | Inputs (mobile `RoundRectangle 8`, web inputs 8px), chips |
| `radius.md` | 10–12 | Buttons 10 (mobile `CornerRadius="10"`, `.nb-auth-submit`); cards 12 (Home cards, web auth card) |
| `radius.lg` | 14 | Sheets, modals |
| `radius.full` | 999 | Avatars only — avoid pill-everything |

**Elevation:** Prefer 1 subtle shadow or 1px border — not stacked shadows.

| Token | Value |
|-------|-------|
| `shadow.card` | `0 1px 2px rgba(32,42,50,0.06)` |
| `shadow.sheet` | `0 8px 24px rgba(32,42,50,0.12)` |

Avoid: neon glow, heavy multi-layer shadows, excessive gradients, decorative clutter.

---

## Iconography

- Style: outlined, 1.5–2px stroke, rounded joins; 20–24px default tap icon.
- Status icons required with colour (sync, order status, payment method, warning).
- Do not rely on colour alone.

Suggested set (Lucide / Material equivalents): Home, Users, ClipboardList, Wallet, MoreHorizontal, Plus, Search, Cloud/CloudOff, RefreshCw, Check, AlertTriangle, X, Chevron, Calendar, Scissors, Shirt, UserPlus, Settings, Receipt.

### Mobile icons (implementation)

MauiIcons.Material only, always sized (tabs 22 · in-field 21 · actions 24 · empty states 48). Single source: [tab-icons.md](../needlebase-mobile/tab-icons.md).

---

## Component library

### Buttons

| Variant | Style | When |
|---------|-------|------|
| Primary | Fill `#247A2B` (`PrimaryDark`), pressed `#1D6B23`, text white | Main CTA (Save, Create order, Record payment) |
| Secondary | White + 1.5 border `#DDE3DF` (`Gray300`), text charcoal; pressed/disabled `Gray100` | Secondary actions. Mobile [code]: `Style="{StaticResource SecondaryButton}"` |
| Tertiary / ghost | Text green or charcoal | Inline, low emphasis |
| Destructive | Fill or text `#DC2626` | Delete, cancel order (confirm first) |
| Disabled | `#E8ECEA` / `#5B656B` (`Gray200` / `Gray600`) | Unavailable |

Heights: mobile 48; web 48 for auth/primary form submit (`.nb-auth-submit`), 40 for dense in-app toolbar buttons. Min tap target 44×44. Focus: 2px ring `#2E9E36` offset 2 (non-text UI, 3:1 is enough).

### Inputs & forms

- Label above field; helper/error below.
- Border `#DDE3DF`; focus border/ring primary green.
- Error: border + text `#DC2626` + icon.
- Measurement fields: numeric keyboard (mobile); tabular figures; clear unit suffix (in / cm).
- Group related measurement sections with section headers, not nested cards.

### Status chips (order)

Always: icon + label (+ optional colour).

Exact token + icon per status (single source): [content-and-states.md §6](content-and-states.md#6-status-names--colours-single-source). Summary: New neutral · Cutting info · Sewing green tint · Fitting amber · Ready to collect solid `PrimaryDark` · Collected muted · Cancelled error muted · Overdue amber badge alongside status.

### Sync chip (persistent)

Compact: icon + short label. Opens Sync details.

See [offline-sync.md](offline-sync.md) for full state copy.

### Cards

White surface, `radius.md`, light border or `shadow.card`, on `#F4F6F5`.  
Home: few strong cards — not a metric wall.  
No cards in marketing-style heroes if any; product UI cards only when they group a task.

### Lists & tables

| Mobile | Web |
|--------|-----|
| Rows with primary + secondary + trailing status/amount | Tables with filters, sort, pagination |
| Swipe sparingly; prefer explicit actions | Row click → detail or drawer |
| Search sticky under title | Search + filter bar above table |

### Navigation

- Mobile bottom nav: 5 items; active = green-tinted icon + label; inactive = tertiary text + untinted/grey icon. Each tab must have its **own** glyph (see Iconography → Mobile icons (implementation)).
- FAB: `PrimaryDark` circle, + or “Order”. **[planned]** (not in code yet).
- Web sidebar (v4): items 44px in an inset tray; active = filled `#247A2B` pill with white text (5.4:1) + soft green glow; inactive = `t2` text. Below 768 the sidebar is an off-canvas drawer (see web-screens.md).

### Dialogs & sheets

- Mobile: bottom sheets for status change, quick payment, filters.
- Web: modal or right drawer for detail/edit without leaving list.
- Destructive: confirm with clear consequence. Feedback surface rules (toast vs dialog vs inline): [content-and-states.md §3](content-and-states.md#3-feedback-which-surface).

### Empty / loading / error

| State | Pattern |
|-------|---------|
| Empty | Illustration sparingly or simple icon + 1 sentence + 1 CTA |
| Loading | Skeleton matching layout (prefer over spinner-only) |
| Error | Icon + plain language + Retry / Go back |
| Offline save success | Toast/banner: “Saved on this device” |

### Receipts

Clean printable layout: business name, customer, order ref, line items, paid/outstanding, method, date, sync caveat if local-only (“Recorded on device — not yet synced”).

---

## Motion

Minimal. 150–200ms ease for sheets/toasts. No decorative looping animation. Sync spinner only while syncing.

## Accessibility checklist

- [ ] WCAG AA contrast for text and critical UI
- [ ] Focus visible on all interactive elements
- [ ] Status = colour + text + icon
- [ ] Form errors associated with fields
- [ ] Touch targets ≥ 44px on mobile
- [ ] Do not convey meaning by colour alone
- [ ] Icon-only buttons have `SemanticProperties.Description` (mobile) / `aria-label` (web) and a stable `AutomationId`
- [ ] One h1 per screen (no duplicate titles — e.g. logo + "Sign in" twice)
- [ ] Large-font setting doesn't clip primary actions (WordWrap, no fixed-height text rows)

Pre-merge gate: [review-checklist.md](review-checklist.md).

## Web v4: soft UI + Material

Approved 22 Oct 2026 (mockup v4). Web only; the MAUI app keeps the flat tokens above. Implemented in `src/NeedleBase.Web/wwwroot/css/site.css` (v4 block) on top of Bootstrap 5 CSS (grid/utilities/forms only; **Bootstrap JS is no longer loaded**).

| Token (`--nb-*`) | Value | Use |
|---|---|---|
| `base` | `#EEF2EF` | Page **and** card surface (soft UI = one surface) |
| `hi` / `lo` | `#FFFFFF` / `#C9D1CC` | Light / dark shadow |
| `edge` | `rgba(32,42,50,.08)` | Hairline on every raised element (shapes stay visible without relying on shadow) |
| `raise` | `-6px -6px 14px hi, 6px 6px 14px lo` (mobile 4/10) | Cards, tables, filter panel |
| `raise-sm` | 3px/8px | Buttons, user card, board cards |
| `inset` | `inset 3px 3px 6px lo, inset -3px -3px 6px hi` | Nav tray, inputs, empty states, icon chips, bar tracks, open `<details>` |
| `radius` | 18 (cards), 12–14 (controls), 99 (chips) | |
| text | ink `#202A32` 12.9:1 · t2 `#4F5A61` 6.3:1 · t3 `#5F6A71` 4.9:1 · link `#247A2B` 4.8:1 · warning `#9A4406` 5.8:1 · error `#B42318` 5.8:1 · focus ring `#247A2B` (all on base; the flat-token `#DC2626`/`#B45309`/`#2E9E36` fail on the soft surface) | |

Type: Roboto/system 14/20; h1 32/40 w400; section titles 14 w500 `t2`; figures tabular. Icons: 24px outlined SVG, stroke 1.5, `aria-hidden`.

Material behaviours: state layer `::after` currentColor .08 hover / .12 focus / .16 active; `:active` scale .98; focus ring `2px solid #247A2B`, offset 3. Primary button = filled `#247A2B` (never `#2E9E36` behind white text: 3.4:1). Outline button = raised; pressed = inset. Chips: tinted bg + dark text ≥5.8:1 with a dot. `prefers-reduced-motion` removes transitions.

Rules: never put text on shadows alone; keep blurs small (≤14px) for paint cost; no inline `style` (CSP) — use classes or SVG attributes (status bar); no gradients beyond the flat surface.

### Web v4 hardening (audit 23 Oct 2026)

- **Logo:** `images/needlebase-logo-clear.png` (transparent, trimmed, 480px) in the sidebar (104px), app bar (68px) and auth card (200px) — the only web logo file (the old boxed `needlebase-logo.png` was removed; it's in git history). Favicon (`favicon.ico`, 16/32/48) and `images/apple-touch-icon.png` (180px, white padding) are cut from its NB mark and linked from both layouts. No web manifest. No `mix-blend-mode` tricks.
- **Forced colours (Windows high contrast):** shadows disappear, so every raised element and inset well gets a `CanvasText` border; active nav/segment = `Highlight` border; status dots/bar keep their colours (`forced-color-adjust: none`).
- **Print:** sidebar/app bar/filters/pager/buttons hidden, shadows removed, cards get a grey border.
- **Long content:** money figures and names wrap (`overflow-wrap:anywhere`), hero figure uses `clamp()`, list rows wrap on phones, money cards stack below 420px.
- **Tables as cards:** cards keep table semantics with explicit `role="table|rowgroup|row|columnheader|cell"` (CSS `display:block` would otherwise drop them).
