# Web Key Screens

Oversight and management tool. Persistent sidebar, denser data, keyboard-friendly. Same identity as mobile.

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
