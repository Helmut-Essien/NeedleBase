# Developer Handoff Notes

Behaviour notes for building screens (MAUI + Razor Pages). Don't invent API contracts beyond the product brief.

---

Breakpoints & responsive rules: [web-screens.md](web-screens.md#responsive-rules-rec). Mobile: design at 360/375/414 wide; large fonts must not break primary actions.

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

## Assets

Logo: supplied SVG/PNG, never recoloured or redrawn · App icon/splash: platform images · **All UI icons: MauiIcons** ([tab-icons.md](../needlebase-mobile/tab-icons.md)) · Empty states: icon-only or spare line art · Web favicon from logo · HelmutCode mark only where attribution is needed.

---

## Copy & localisation readiness

- UI strings externalisable; default English (Ghana-friendly)  
- Phone, dates (`dd MMM yyyy`, Africa/Accra), money (GH₵, 2 dp), microcopy and API-error mapping: **[content-and-states.md](content-and-states.md)** (single source; don't restate here)  

---

## Accessibility handoff

- AA contrast verified for text on green/white/grey  
- Announce sync status changes (screen reader live region on chip update)  
- Do not use colour-only status  
- Hit targets ≥44px mobile  
- Font scale "Largest" + TalkBack pass on touched screens  

---

## Definition of done (UI)

Run [review-checklist.md](review-checklist.md). Never claim: full web offline parity, provider-confirmed payments without provider + sync success, backend features outside the brief, or a central platform admin.
