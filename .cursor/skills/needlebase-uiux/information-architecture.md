# NeedleBase Information Architecture

## Domain entities (shared)

```
Business (workspace)
├── Staff / Invitations (Owner, Manager, Tailor)
├── Customers
│   ├── Measurement profiles (+ garment templates, history)
│   ├── Reference images & notes
│   └── Orders
│       ├── Garment line items (snapshot measurements, style images, notes)
│       ├── Production status & assignments
│       ├── Fitting / delivery dates
│       └── Payments (deposits, instalments) → Receipts
└── Settings (units, currency GHS, notifications, subscription, sync)
```

**Order statuses (main flow):** New → Cutting → Sewing → Fitting → Ready → Collected (UI label for Ready: **Ready to collect**; colours/icons in [content-and-states.md](content-and-states.md#6-status-names--colours-single-source))  
**Side statuses:** Cancelled, Overdue (flag on due date; do not clutter primary chip set)

---

## Mobile navigation map

### Shell

```
┌─────────────────────────────────────┐
│  Top: greeting / title + SyncChip   │
│  Content                            │
│  [FAB: New Order]                   │
├─────────────────────────────────────┤
│ Home | Customers | Orders | Payments│ More │
└─────────────────────────────────────┘
```

| Tab | Purpose | Primary children | Tab icon (MauiIcons) | Built? |
|-----|---------|------------------|----------------|--------|
| **Home** | “What do I need to do today?” | Due/overdue, ready for collection, balances, recent activity, quick actions | `Home` | ✓ (Slice 2) |
| **Customers** | Find & manage people | List → Profile (Overview, Measurements, Orders, Images/Notes) → Add/Edit | `People` | ✓ |
| **Orders** | Daily production work | List/filter → Detail → Status update; Create multi-step | `ReceiptLong` | ✓ (Slice 3) |
| **Payments** | Money in | Balances, record payment, history, receipt share | `Payments` | [code] Slice 5 (Owner/Manager only) |
| **More** | Secondary | Measurements hub (optional shortcut), Staff*, Settings, Sync details, Subscription, Help | `Menu` | Placeholder + About (Terms/Privacy) built; rest planned |

Tab icons: MauiIcons.Material on Shell tabs — [tab-icons.md](../needlebase-mobile/tab-icons.md).

\*Staff only if business has staff or Owner/Manager opted in — solo: omit or show “Invite helper” soft entry.

**Primary action:** FAB **New Order** (visible on Home, Customers, Orders; optional on Payments). **[planned]** No FAB in code yet; today Home has a "New order" quick action.

### Auth & onboarding (pre-shell)

```
Welcome
├── Sign up → Verify → Business profile setup → Offline explainer → Trial/activation → Home
├── Log in → Home (or Accept invite → role-limited Home)
└── Password recovery  (hidden until built — no link shown)
```

Offline explainer: one screen — “Works without internet. Saves on this device. Syncs when you’re back online.”

**Mobile chrome:** single source is [needlebase-mobile hard rule 14](../needlebase-mobile/SKILL.md).

### Mobile screen inventory

| Area | Screens |
|------|---------|
| Auth | Welcome, Sign up, Login, Forgot password, Invite accept |
| Setup | Business profile, Units/currency, Offline explainer, Trial active |
| Home | Dashboard |
| Customers | List, Add/Edit, Profile tabs |
| Measurements | Template picker, Entry form, History compare, Linked from customer/order |
| Orders | List, Create (steps), Detail, Status sheet, Calendar (fitting/delivery) |
| Payments | List/balances, Record payment, Receipt preview |
| More | Staff list/invite/roles*, Settings, Sync details, Subscription |
| System | Empty, loading, error, conflict resolve, subscription expired sheet |

### Role-filtered mobile nav

| Item | Owner | Manager | Tailor |
|------|-------|---------|--------|
| Home | ✓ full | ✓ full | ✓ assigned-focused |
| Customers | ✓ | ✓ | ✓ relevant only |
| Orders | ✓ | ✓ | ✓ assigned |
| Payments | ✓ | ✓ | ✗ or view-only if policy allows (default: hide record) |
| More → Staff | ✓ | ✓ | ✗ |
| More → Settings/Subscription | ✓ | limited | profile + sync only |
| FAB New Order | ✓ | ✓ | per permission (default ✓ if assigned work model allows create) |

Default MVP: Tailor can update production status; cannot manage staff or subscription; payment recording Owner/Manager only unless Owner enables.

---

## Web navigation map

Sidebar (planned S6): Dashboard · Customers · Measurements · Orders · Production · Payments · Staff* · Reports · Settings. Top bar: business, search, SyncChip, profile. \*Staff hidden for solo until invited.

| Nav item | Purpose |
|----------|---------|
| **Dashboard** | Active/overdue, deadlines, balances, recent payments, workload by status, activity, quick actions |
| **Customers** | Table + detail (tabs: Overview, Measurements, Orders, Payments, Notes & Images) |
| **Measurements** | Cross-customer measurement work + templates; deep link from customer |
| **Orders** | Filterable table; detail; bulk useful actions |
| **Production** | Board grouped by status; assignment; fitting/delivery calendar |
| **Payments** | List, balances, receipts; simple outstanding/revenue summary |
| **Staff** | Invite, roles, profiles, assignments |
| **Reports** | Lightweight operational summaries (not full accounting) |
| **Settings** | Business, units, currency, notifications, subscription, sync diagnostics |

### Web responsive nav

| Breakpoint | Behaviour |
|------------|-----------|
| ≥1200px | Persistent left sidebar expanded |
| 768–1199px | Collapsible icon sidebar or overlay drawer |
| <768px | Hamburger drawer + stacked cards instead of wide tables (no bottom nav on web; bottom nav is the mobile app's) |

**Current code:** web `_Layout` is a Bootstrap top navbar (`navbar-expand-md`, collapses below 768; links: Home + user + Sign out; footer links Terms/Privacy). The sidebar above is **[planned Slice 6]** and must also collapse at `md` (768), matching [handoff.md](handoff.md).

### Role-filtered web nav

| Item | Owner | Manager | Tailor |
|------|-------|---------|--------|
| Dashboard | ✓ | ✓ | ✓ limited (own work) |
| Customers | ✓ | ✓ | ✓ relevant |
| Measurements | ✓ | ✓ | ✓ as needed for orders |
| Orders | ✓ | ✓ | ✓ assigned |
| Production | ✓ | ✓ | ✓ own columns/cards |
| Payments | ✓ | ✓ | ✗ (default) |
| Staff | ✓ | ✓ | ✗ |
| Reports | ✓ | ✓ | ✗ |
| Settings | ✓ full | ops prefs | account + sync |

---

## Cross-links (both platforms)

| From | To |
|------|----|
| Customer | New order, measurements, payment, history |
| Order | Customer, measurement snapshot, payments, assign staff, receipt |
| Payment | Order, customer, receipt |
| Home/Dashboard tiles | Filtered Orders / Payments lists |
| Sync chip | Sync details (pending, retry, conflicts) |
| Subscription badge | Plan / renew (never blocks login) |

## Empty-state destinations

Every list (Customers, Orders, Payments, Staff, Production columns) offers one clear CTA aligned to the workflow (e.g. “Add your first customer”, “Create order”).
