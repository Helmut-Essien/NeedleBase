# Web Key Screens

Oversight and management tool. Persistent sidebar, denser data, keyboard-friendly. Same identity as mobile.

**Built today [code]:** Razor Pages `Account/Login`, `Account/Register`, `Account/Logout`, `Index`, `Terms`, `Privacy`, `Error`, plus Slice 5/6 pages `Production`, `Orders/Index`, `Orders/Details` (status change is the only web write), `Payments`, `Customers`, `Activity`, with `_AuthLayout` (auth card 448px, system font, no CDN) and `_Layout` (left `.nb-sidebar` from md 768 up; below md a top bar with a collapsible menu; skip link; footer Terms/Privacy). Shared partials: `_PageState` (error + Try again), `_StatusChip`, `_Pager`, `_ActivityItem`, `_NavLinks`, `_PasswordToggleButton`. No Forgot password link until reset is built. Sections below still marked **[planned]** are not built.

## Responsive rules [rec]

| Width | Layout |
|-------|--------|
| <768 | Drawer nav; **tables become cards** (one card per row: primary line, secondary line, trailing status/amount, tap → detail); filters in an offcanvas; sticky primary action at the bottom |
| 768–1199 | Icon sidebar; tables keep 4–5 key columns (hide secondary columns with `d-none d-lg-table-cell`) |
| ≥1200 | Full sidebar + full tables; optional split detail |

Many owners will open the web app **on a phone**, so the <768 layout is a first-class case. Money columns right-aligned, tabular figures. Page `max-width` 1200–1440.

Sample business: **Ama Stitches**, Accra. Currency **GH₵**.

---

## App shell

- **Left sidebar:** Dashboard, Customers, Measurements, Orders, Production, Payments, Staff*, Reports, Settings  
- **Top bar:** Business selector (if multi-business later), global search, SyncChip, profile menu  
- **Main:** Page title + primary actions; tables/boards/detail  

\*Staff hidden for solo until invite.

Narrow: collapse sidebar; tables → stacked cards.

---

## Dashboard

Operational focus (no vanity metrics):

- Active & overdue orders  
- Upcoming fittings/deliveries (calendar strip)  
- Outstanding balances  
- Recent payments  
- Production workload by status (counts + link to board)  
- Recent activity  
- Quick actions: New order, Add customer, Record payment  

SyncChip always visible in top bar.

---

## Customers & measurements

| View | Behaviour |
|------|-----------|
| Customers table | Search, phone, open orders, balance; pagination |
| Customer detail | Tabs: Overview · Measurements · Orders · Payments · Notes & Images |
| Measurements | Large-screen form: multi-column sections; history compare side-by-side; templates |
| Actions | Create order, record payment, invite-related n/a |

---

## Orders

| View | Behaviour |
|------|-----------|
| Table | Filters: status, due date, customer, staff; sort; pagination |
| Detail | Full order; status; assignment; payment ledger; measurement snapshot read-only with edit link |
| Bulk | Where useful (e.g. assign, status) — confirm first; no silent financial bulk |

Status chips: colour + text + icon.

---

## Production board

- Columns by status: New | Cutting | Sewing | Fitting | Ready (| Collected optional archived)  
- Cards: ref, customer, garment summary, due, assignee avatar  
- Drag or explicit Move + Assign  
- Toggle: Board | Calendar (fitting/delivery)  
- Overdue: warning marker on card  

Tailor role: see assigned cards primarily.

---

## Payments

- Searchable list: date, customer, order, method, amount, sync state  
- Balance summaries: outstanding total, collected period (simple — not full accounting)  
- Receipt preview panel / print  
- Record payment drawer  

Local pending payments show “On device / waiting to sync” — never provider-confirmed.

Destructive and money actions use a Bootstrap confirm modal naming the record and the consequence ([content-and-states.md §3](content-and-states.md#3-feedback-which-surface)).

---

## Staff

- Invitations (pending/accepted)  
- Role assignment with plain-language permission summary  
- Profiles; order assignment counts  
- Owner language: “Manager runs the shop day to day. Tailors see work assigned to them.”

---

## Settings & subscription

- Business profile  
- Units, currency (GHS default)  
- Notification preferences  
- Subscription: plan, renewal, usage limits, renew/upgrade  
- Sync diagnostics + link to pending/conflicts  
- Expired: available vs restricted list (see offline-sync)

---

## Web patterns

| Pattern | Use |
|---------|-----|
| Table + filters | Primary lists |
| Right drawer | Quick edit / record payment / assign |
| Tabs | Customer & order detail sections |
| Board | Production only |
| Confirm modal | Destructive / bulk / conflict |

Do not claim full offline parity with mobile; show local-save and sync protection for edits.


## Web front-end rules [code, Oct 2026 UX pass]
- **Same origin only.** No CDN fonts/scripts; system font stack everywhere (auth included). A CSP (`default-src 'self'`, `script-src 'self'`, `style-src 'self'`, `frame-ancestors 'none'`, `form-action 'self'`; dev adds `ws:` for hot reload) is set in `Program.cs`, so **no inline `<script>`, `style=""` or `on*=` handlers**: add a class in `site.css` or code in `wwwroot/js/site.js`.
- **Every `<link>`/`<script>` uses `asp-append-version="true"`** so `MapStaticAssets` serves fingerprinted, immutable, br/gzip assets in production. Scripts are `defer`. jQuery + validation load only on forms that need them (`_ValidationScriptsPartial` includes jQuery); Bootstrap JS only on `_Layout` (mobile menu collapse).
- **Click feedback:** `site.js` disables the clicked submit button once a form actually submits (after client validation), sets `aria-busy`, and swaps to `data-pending-text` ("Signing in…", "Saving…"). Restored on back/forward (pageshow). Give every POST button a `data-pending-text`.
- **Empty states:** one message, no empty scaffolding (e.g. Production hides the empty board columns when there are no active orders). Pluralise counts ("1 order", "2 orders").
- **Contrast:** web `--nb-text-tertiary` is `#69737A` (4.7:1) and `--nb-warning` text `#B45309` (5:1); links use PrimaryDark `#247A2B`, never Primary `#2E9E36` on white for text (3.4:1).
- **Images:** ship at ≤2× display size (logo 680×376, ~24 KB palette PNG).
- `prefers-reduced-motion` disables transitions.
