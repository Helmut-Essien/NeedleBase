# Developer Handoff Notes

Target stack: **.NET MAUI** (mobile), **modern responsive web** + **ASP.NET Core API**. UI agents document behaviour; do not invent API contracts beyond what product defines.

---

## Responsive breakpoints (web)

| Name | Width | Layout |
|------|-------|--------|
| `sm` | <768px | Stacked cards; drawer nav; single column forms |
| `md` | 768–1199px | Collapsible sidebar; tables may scroll horizontally or reduce columns |
| `lg` | ≥1200px | Persistent sidebar; full tables; optional split detail |

Content max width ~1200–1440px inside main pane.

Mobile: design at 375 and 414; large text / accessibility fonts must not break primary actions.

---

## Interaction behaviour

| Pattern | Mobile | Web |
|---------|--------|-----|
| Primary create | FAB New Order | Page header button |
| Quick edit | Bottom sheet | Right drawer or modal |
| Filters | Sheet | Filter bar / dropdowns |
| Navigation | Bottom tabs | Sidebar routes |
| Mobile chrome | **No Shell nav title bar on any screen** — in-page titles + Back on pushed pages | Dedicated `_AuthLayout` (no app sidebar) |
| Destructive | Confirm dialog | Confirm modal |
| Save offline | Toast + pending badge | Banner/badge if local draft supported |
| Focus | System focus; 48px controls | Visible focus ring; keyboard tab order |

### Form behaviour

- Validate on submit; inline field errors  
- Measurement: numeric inputMode; preserve unit  
- Dirty form: warn on back/navigate  
- Disable double-submit; show progress on primary button  

### Order status

- Forward flow preferred; allow controlled rollback with reason if product allows  
- Cancelled exits main board (filterable)  
- Overdue = derived from due date + not Collected/Cancelled — badge, not separate column required  

---

## Component states to implement

Every interactive component: **default · hover (web) · focus · pressed · disabled · loading · error** (as applicable).

Critical composites:

- SyncChip (all sync states)  
- StatusChip (order statuses + overdue)  
- Payment row (pending sync vs synced)  
- Customer/Order list row  
- Primary Button / FAB  
- TextField / MeasurementField  
- EmptyState / ErrorState / Skeleton  

Tokens: see [design-system.md](design-system.md).

---

## Asset requirements

| Asset | Spec |
|-------|------|
| NeedleBase logo | Supplied SVG/PNG; light & dark-safe if needed; do not recolour outside brand guide |
| HelmutCode mark | Only where parent attribution required (e.g. About) |
| App icon | Adaptive Android + iOS; green brand + needle metaphor if supplied |
| **Mobile UI icons** | No custom tab PNG/SVG set — use **MauiIcons.Material** enums (`Home`, `People`, `ReceiptLong`, `Payments`, `Menu`, plus in-page actions). See [tab-icons.md](../needlebase-mobile/tab-icons.md). |
| Empty-state illustrations | Optional, monochrome/line, spare — or MauiIcon-only |
| Favicon (web) | From logo |

Export: no UI icon bitmap pack required for mobile chrome; PDF/PNG OK for receipt logo / app icon / splash.

---

## Copy & localisation readiness

- UI strings externalisable; default English (Ghana-friendly)  
- Phone display: preserve local formats  
- Dates: locale-aware; relative on Home (“Due today”)  
- Money: GH₵ with 2 decimals; tabular figures  

---

## Accessibility handoff

- AA contrast verified for text on green/white/grey  
- Announce sync status changes (screen reader live region on chip update)  
- Do not use colour-only status  
- Hit targets ≥44px mobile  

---

## Definition of done (UI)

- [ ] Matches IA for platform  
- [ ] Uses design tokens (no one-off hex sprawl)  
- [ ] Offline/sync copy honest  
- [ ] Empty/loading/error/recovery present  
- [ ] Role-appropriate nav  
- [ ] Sample data realistic  
- [ ] Handoff notes updated for new components  

---

## What not to claim

- Full web offline parity (unless built)  
- Payment provider confirmation without provider + sync success  
- Backend features not in product brief  
- Central platform admin for NeedleBase accounts  
