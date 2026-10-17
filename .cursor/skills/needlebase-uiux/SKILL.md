---
name: needlebase-uiux
description: >-
  Designs and implements NeedleBase UI/UX for mobile (.NET MAUI) and responsive
  web using the product design system, information architecture, offline-first
  sync states, and priority journeys. Use when designing screens, building UI,
  creating components, writing copy, prototyping flows, or reviewing NeedleBase
  interface work for tailoring business management (customers, measurements,
  orders, production, payments, staff, subscription).
---

# NeedleBase UI/UX

Offline-first tailoring business management SaaS (HelmutCode). Mobile is the daily tailor tool; web is oversight. Shared visual identity; platform-specific density and patterns.

## When this skill applies

Use for any NeedleBase interface work: screens, components, navigation, empty/loading/error states, offline/sync UX, subscription UX, prototypes, or developer handoff.

## Design order (always)

1. Confirm IA + navigation for the platform — [information-architecture.md](information-architecture.md)
2. Apply tokens and components — [design-system.md](design-system.md)
3. Design/implement the screen using [mobile-screens.md](mobile-screens.md) or [web-screens.md](web-screens.md)
4. Wire offline/sync/subscription honesty — [offline-sync.md](offline-sync.md)
5. Validate against priority journeys — [journeys.md](journeys.md)
6. Capture handoff notes when delivering — [handoff.md](handoff.md)

## Product personality

Professional, trustworthy, modern but approachable, practical, efficient, clean, organised, dependable.

**Not:** generic admin CRUD, vanity dashboards, neon/gradient decoration, heavy shadows, overly rounded pills, purple AI-default themes.

## Core workflow (must stay first-class)

Customer registration → Measurements → Sewing order → Production tracking → Payment collection → Garment collection

## Roles (plain language)

| Role | Sees / does |
|------|-------------|
| Owner | Full control |
| Manager | Operations + staff coordination |
| Tailor | Assigned work + relevant customer/order info only |

- Solo tailor: never force staff/branch setup; hide or simplify staff features.
- Multi-employee: assignment, progress, payments supervision.
- Each business = independent workspace; never mix records.

## Platform split

| | Mobile (MAUI) | Web |
|---|---------------|-----|
| Job | Everyday tailor work | Business oversight |
| Nav | Bottom 5 + New Order FAB | Left sidebar |
| Offline | Full MVP offline required | Clear local-save/sync; do not claim full offline parity unless implemented |
| Density | Touch-first, large tap targets | Tables, filters, drawers, keyboard |

## Non-negotiable rules

1. **Offline honesty** — After offline save, confirm “saved on this device.” Never claim cloud sync. Never present unsynchronised payment as confirmed by an external provider.
2. **Sync visibility** — Persistent but unobtrusive sync indicator on major shells (Home / top bar).
3. **Status accessibility** — Colour + text + icon for all statuses (orders, sync, payments, subscription).
4. **Logo** — Use supplied NeedleBase logo; do not redesign or distort.
5. **Currency** — Default GHS; design for future multi-currency without inventing backend features.
6. **Sample data** — Realistic Ghanaian / West African names, phone formats, garment types, GHS amounts.
7. **No invented capabilities** — Do not claim APIs, providers, or offline features outside the brief.

## Copy tone

Concise, friendly, non-technical. Prefer: “Saved on this phone”, “Waiting to sync”, “Payment recorded locally”, “Couldn’t sync — try again”.

## Anti-patterns

- Metric overload on Home / Dashboard
- Colour-only status chips
- Silent overwrite of payments or financial conflicts
- Subscription banner dominating everyday work or blocking login
- Staff invitations forced on solo onboarding
- Claiming “synced to cloud” when only local save succeeded
- **Shell/nav title bar on login/register (or other pre-shell auth)** — duplicate grey strip with “Sign in” / “Register” above the in-page heading; hide `Shell.NavBarIsVisible` (see [needlebase-mobile](../needlebase-mobile/SKILL.md))

## Output expectations

When designing or implementing UI:

- Prefer reusable design-system components over one-off styles
- Include empty, loading, error, and recovery for every major flow touched
- Show sync state where data can be pending
- Note breakpoint / interaction behaviour in handoff when delivering screens
- **Performance UX** — Design for virtualized long lists, progressive/skeleton loading, and visible-first content; avoid screens that require loading entire histories before first paint (see [code-quality-performance](../needlebase-architecture/code-quality-performance.md))
- **Inspect before coding** — When changing implemented UI, follow [engineering-review](../needlebase-architecture/engineering-review.md): inspect existing screens/flows first; fix root UX/code cause; remove obsolete screens/handlers; verify with evidence

## Reference map

| Need | File |
|------|------|
| Nav maps, roles, IA | [information-architecture.md](information-architecture.md) |
| Colours, type, spacing, components | [design-system.md](design-system.md) |
| Sync, conflict, subscription states | [offline-sync.md](offline-sync.md) |
| Mobile screen specs | [mobile-screens.md](mobile-screens.md) |
| Web screen specs | [web-screens.md](web-screens.md) |
| Five priority journeys | [journeys.md](journeys.md) |
| Breakpoints, assets, handoff | [handoff.md](handoff.md) |
| Mobile icons (MauiIcons.Material — tabs + in-page) | [../needlebase-mobile/tab-icons.md](../needlebase-mobile/tab-icons.md) |

## Implementation skills (code)

When scaffolding or coding (not just designing), also use:

| Need | Skill |
|------|-------|
| Clean Architecture + solution layout | [needlebase-architecture](../needlebase-architecture/SKILL.md) |
| Implementation slices / phases | [needlebase-phases](../needlebase-phases/SKILL.md) |
| Domain, tenancy, DTOs | [needlebase-domain](../needlebase-domain/SKILL.md) |
| MAUI MVVM | [needlebase-mobile](../needlebase-mobile/SKILL.md) |
| Offline SQLite sync | [needlebase-mobile-sync](../needlebase-mobile-sync/SKILL.md) |
| ASP.NET Api | [needlebase-api](../needlebase-api/SKILL.md) |
| Responsive web | [needlebase-web](../needlebase-web/SKILL.md) |
