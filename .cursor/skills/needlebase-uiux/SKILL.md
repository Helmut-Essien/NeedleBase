---
name: needlebase-uiux
description: >-
  NeedleBase UI/UX rules for MAUI mobile and Razor web: design tokens mapped to
  Colors.xaml/site.css, measured contrast, validation UX, proven MAUI patterns
  (password toggle, wrapping rows, tab bar, double-back), Figma workflow and a
  pre-merge review checklist. Use when designing or building screens, components
  or copy, implementing Figma frames, or reviewing NeedleBase UI changes.
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
7. Before merge, run [review-checklist.md](review-checklist.md)

## Figma workflow

File key `8qquGru92p56agRvfSRBie` — pages **Auth — Login & Register**, **Mobile — Home**. Pull a frame with the Figma MCP (`get_design_context` / `get_screenshot` on the node), then map its colours to existing tokens ([design-system.md](design-system.md)) — never paste Figma hex. Figma wins on layout; the skill rules win on contrast, data honesty and offline copy. Record conflicts in the PR.

## Product personality

Professional, trustworthy, modern but approachable, practical, efficient, clean, organised, dependable.

**Not:** generic admin CRUD, vanity dashboards, neon/gradient decoration, heavy dark shadows (web v4 soft UI uses small light/dark pairs only — see design-system.md § Web v4), overly rounded pills, purple AI-default themes.

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
| Nav | Bottom 5 + New Order FAB | Left sidebar; <768 top app bar + collapsible drawer |
| Built today | Auth, Home, Customers, Measurements, Orders (list/create/detail); Terms/Privacy; Payments placeholder | Auth, Dashboard, Production, Orders (list/detail + status), Payments, Customers, Activity, Terms/Privacy (sidebar ≥768, top bar below) |
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
8. **No fake data on real screens** — sample data is for prototypes; shipped screens show real values or honest zero/empty states.

## Copy tone

Concise, friendly, non-technical; icons **with** labels (low-literacy friendly). Prefer: “Saved on this device”, “Waiting to sync”, “Payment recorded locally”, “Couldn’t sync — try again”.

## Anti-patterns

- Metric overload on Home / Dashboard (KPI cards only if actionable + real data, max 2 — see [mobile-screens.md](mobile-screens.md#home-dashboard))
- Colour-only status chips
- Silent overwrite of payments or financial conflicts
- Subscription banner dominating everyday work or blocking login
- Staff invitations forced on solo onboarding
- Claiming “synced to cloud” when only local save succeeded
- Entry points to unbuilt features (Notifications bell): hidden until the real screen ships. Forgot password shows only when email is configured
- Shell nav title bar on any mobile screen (rule: [needlebase-mobile #14](../needlebase-mobile/SKILL.md))

## Output expectations

When designing or implementing UI:

- Prefer reusable design-system components over one-off styles
- Include empty, loading, error, and recovery for every major flow touched
- Show sync state where data can be pending
- Note breakpoint / interaction behaviour in handoff when delivering screens
- **Performance UX** — Design for virtualized long lists, progressive/skeleton loading, and visible-first content; avoid screens that require loading entire histories before first paint (see [code-quality-performance](../needlebase-architecture/code-quality-performance.md))
- **Inspect before coding** — When changing implemented UI, follow [engineering-review](../needlebase-architecture/engineering-review.md): inspect existing screens/flows first; fix root UX/code cause; remove obsolete screens/handlers; verify with evidence

## Reference map

**Build-time (read when coding UI):**

| Need | File |
|------|------|
| Tokens ↔ Colors.xaml/site.css, contrast, components | [design-system.md](design-system.md) |
| Formatting (GH₵, dates, +233), microcopy, feedback surfaces, states, status colours, API errors, a11y, low-end perf | [content-and-states.md](content-and-states.md) |
| MAUI patterns & gotchas | [maui-patterns.md](maui-patterns.md) |
| Field errors | [validation.md](validation.md) |
| Mobile / web screen specs (built vs planned) | [mobile-screens.md](mobile-screens.md) · [web-screens.md](web-screens.md) |
| Sync/offline/conflict/subscription UX | [offline-sync.md](offline-sync.md) |
| Icons (single source) | [tab-icons.md](../needlebase-mobile/tab-icons.md) |
| Pre-merge gate | [review-checklist.md](review-checklist.md) |

**Design-time (planning, prototypes, IA changes):** [information-architecture.md](information-architecture.md) · [journeys.md](journeys.md) · [handoff.md](handoff.md).

Code-level skills: [needlebase-mobile](../needlebase-mobile/SKILL.md) (MAUI), [needlebase-web](../needlebase-web/SKILL.md) (Razor), [needlebase-architecture](../needlebase-architecture/SKILL.md) (layers, testing).

## Activity log copy (22 Oct 2026)

Plain past tense with the order ref (“Order #ORD-12: New → Cutting”, “Voided payment GH₵ 50.00 (Cash): entered twice”). Times “22 Oct 2026, 14:05” Accra. Hidden values show “(hidden)”, never the value. Empty: “No activity yet…”; filtered empty: “Nothing matches these filters.” Details are a native disclosure (keyboard + 44px target).
