# NeedleBase Information Architecture

How the phone app and the website are organised: tabs, sidebar, roles and main journeys.

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

**Order statuses (main flow):** New → Cutting → Sewing → Fitting → Ready → Collected (UI label for Ready: **Ready to collect**; colours/icons in [content-and-states.md](content-and-states.md#6-status-names-and-colours))  
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
| **More** | Secondary | Sync details, Change password, Delete account, Terms, Privacy, Check for updates, Staff*, WhatsApp settings* | `Menu` | Built as listed. A Subscription / plan page is not built. Measurements stay under Customers, not as a More shortcut. |

Tab icons: MauiIcons.Material on Shell tabs — [tab-icons.md](../needlebase-mobile/tab-icons.md).

\*Staff only if business has staff or Owner/Manager opted in — solo: omit or show “Invite helper” soft entry.

**Primary action:** FAB **New Order** (visible on Home, Customers, Orders; optional on Payments). **[planned]** No FAB in code yet; today Home has a "New order" quick action.

### Auth & onboarding (pre-shell)

```
Welcome
├── Sign up → Verify → Business profile setup → Offline explainer → Home
├── Log in → Home (or Accept invite → role-limited Home)
└── Password recovery  (Login shows the link when email is configured)
```

There is no free trial. Registering a cloud shop uses a licence key when Platform requires one. A Free phone-only mode with no account is decided but **not built** (see the README section "Pricing (decided, not built)").

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

Sidebar **[code]**: Dashboard · Production · Orders · Customers · Payments* · Staff* · WhatsApp* · Activity* · License (Owner). Measurements live on the customer pages (no Measurements nav item). Reports are **not built**. Top bar: business name, user, Sign out (no SyncChip or global search). Staff stays visible for a solo Owner so the invite entry point is reachable. \*Role-filtered.

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
| ≥1200px | Persistent left sidebar (272px), full tables |
| 768–1199px | Persistent sidebar (240px), tighter padding, 2-column metric grid |
| <768px | Top app bar + collapsible off-canvas drawer (hamburger) + stacked cards instead of wide tables (no bottom nav on web; bottom nav is the mobile app's) |

**Current code:** web `_Layout` has the left sidebar from `md` (768) up (Dashboard, Production, Orders, Customers, Payments, Activity) and below 768 a top app bar whose hamburger opens the same sidebar as an off-canvas drawer (see web-screens.md § App shell); footer links Terms/Privacy. Remaining sidebar items are planned.

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
