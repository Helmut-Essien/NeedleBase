# Mobile Key Screens

Primary daily tool for tailors. Touch-first, offline-capable, uncluttered for busy shop floors.

Sample business: **Ama Stitches**, Accra. Currency **GH₵**.

---

## Onboarding & authentication

**Chrome rule (mobile implementation):** **Every** screen hides the Shell navigation title bar (`Shell.NavBarIsVisible="False"` on AppShell + each page). Auth/onboarding also hide the tab bar. Headings and Back live in-page. See [needlebase-mobile](../needlebase-mobile/SKILL.md) hard rule 14 and [di-navigation.md](../needlebase-mobile/di-navigation.md).

| Screen | Content | Notes |
|--------|---------|-------|
| Welcome | Logo, short promise (“Manage orders & measurements — works offline”), Log in / Create account | Brand-first, no metric clutter; no Shell nav bar |
| Sign up | Name, phone/email, password | Clear validation; no Shell nav bar |
| Login | Phone/email + password; Forgot password | In-page “Sign in” only |
| Password recovery | Phone/email → instructions | **Hidden until built** (decision). No Forgot password link on mobile or web; add the link in the same change that ships the reset flow |
| Business profile | Business name, phone, location (optional), logo optional | Solo-friendly; no staff required |
| Offline explainer | One screen: local save + sync later | Mandatory once; no Shell nav bar |
| Trial / activation | Plan summary + Start | Soft; then Home |
| Accept invite | Shop name + role explanation → Join | No Shell nav bar |

---

## Home dashboard

**Question answered:** What do I need to do today?

Structure (top → bottom):

1. Greeting + **business name** + SyncChip  
2. Quick actions row: New Order · Add Customer · Record Payment  
3. **Needs attention** — overdue / due today (order cards)  
4. **Ready for collection**  
5. **Outstanding balances** (top few)  
6. Recent activity (compact)

**KPI rule (resolves Figma vs this spec):** a number card is allowed only if it is (1) **actionable**, meaning tapping it opens the filtered list it counts, and (2) **backed by real data**, showing a real count or an honest 0, never a sample value. At most 2 KPI cards. Anything else (revenue totals, growth %, charts) is a vanity KPI and stays off Home.

**Implemented Home [code, from Figma *Mobile — Home*]:** header (initials + greeting + business name; **no notifications bell** — removed until a real screen exists), a **SyncChip** (Slice 4: honest sync state, opens Sync details), KPI cards **Active orders** and **Ready to collect** (`ActiveOrdersCount`, `ReadyToCollectCount`), 4 quick actions (Add customer · New order · Payment · Measure), **Orders in progress** by stage + View all, and **Needs attention** with the empty state "Nothing needs attention right now". Each KPI opens Orders already filtered to what it counts: **Active orders** → filter **Active** (`OrderStatus.Active` = New, Cutting, Sewing, Fitting, Ready; the same list Home counts), **Ready to collect** → **Ready to collect**, **View all** → **All**. Deep link `//main/OrdersPage?filter=<key>` (`OrdersViewModel.ApplyQueryAttributes`); a plain Orders tab tap keeps the filter the picker shows. Empty states: "No active orders. Everything is collected or cancelled." / "Nothing is ready to collect right now." Open item: the "Offline-ready" card is replaced by the SyncChip in Slice 4.

Strong hierarchy; few cards.

---

## Customers

| Screen | Behaviour |
|--------|-----------|
| List | Search; avatar/initials, name, phone; trailing open-order count |
| Add/Edit | Name (required), phone (required), optional photo, notes |
| Profile | Header + actions (Create order, Record payment) |
| Tabs | Overview · Measurements · Orders · Images/Notes |

---

## Measurements (core)

Optimised for **in-person measuring**.

- Customer measurement profiles  
- Garment templates: shirt, trousers, dress, skirt, suit, traditional (e.g. kaftan, smock, slit & kaba)  
- Adaptable sections; unit toggle cm/in (business default in Settings)  
- Numeric keyboard; clear labels; easy next-field  
- Notes, date, history  
- Previous vs current comparison  
- Reuse / snapshot onto new orders  

Entry layout: section headers + stacked fields; sticky Save; “Saved on this device” when offline.

---

## Orders & production

| Screen | Behaviour |
|--------|-----------|
| List | Search/filter by status, due; status chip + overdue flag |
| Create (guided) | 1 Customer → 2 Garments/qty/templates → 3 Measurements snapshot → 4 Style images/notes → 5 Price & deposit → 6 Dates & assign (if staff) → Review |
| Detail | Ref, customer, garments, status timeline, payments summary, assign, dates |
| Status update | Bottom sheet with statuses + optional note |
| Calendar | Fitting / delivery agenda |

Statuses: New → Cutting → Sewing → Fitting → Ready → Collected (+ Cancelled, Overdue flag). Chip colours/icons: [content-and-states.md §6](content-and-states.md#6-status-names--colours-single-source). **Orders list + create wizard + detail/status are [code] (Slice 3).** Deposit step deferred to Slice 5.

**Order detail (code, 23 Oct 2026):** each garment is a white card: "2 × Dress", line total, a **Measurements** block with "Taken 1 Sep 2026" (or "Saved with this order (date not recorded)" for lines made before the date was stored) and a two-column value grid in template order; with no snapshot: "No measurements saved with this order" + **View customer's measurements**. **Open customer profile** under the customer. **Edit order** shows for Owner/Manager on non-locked orders only.
**Edit order (code):** `EditOrderPage` (`EditOrderPage?orderId=`): due date switch + date, order notes, one card per garment (quantity, price in GH₵, notes, Remove; garment and measurements are fixed, "Measurements kept · Taken …"), "Add garment" picker (a new garment snapshots the customer's latest measurements for that garment, if any), live "New total: GH₵ …", Save/Cancel. Rules: at least one garment, ≤ 50, total may not fall below what's paid, Tailors and locked orders refused, stale edits reload with an explanation. Saves locally and queues for sync ("Saved on this device"). Every input has a SemanticProperties description.

---

## Payments & receipts

**[code] Slice 5**: methods from domain (Cash, MoMo, Bank, Other). NeedleBase *records* payments; it doesn't process MoMo (Terms §5.1), so copy must never say "paid via MoMo" as if confirmed by the network. Use "MoMo payment recorded".

- Customer / order balances  
- Record payment: amount, method (Cash, MoMo, Bank, Other), order link, note  
- History list with sync badges on pending  
- Receipt preview → Share / Print  
- Distinguish local cash vs any online provider (see offline-sync)

Default currency GH₵.

---

## Staff & settings (More)

**[planned]**: More tab contents. Built so far [code]: placeholder intro + **About** card with Terms and Conditions / Privacy Policy rows (push `TermsPage`/`PrivacyPage`) + app version. Everything else here is planned.

| Solo | Multi-employee |
|------|----------------|
| Soft “Invite someone to help” | Staff list, invite, role (Owner/Manager/Tailor), plain-language permissions |
| Settings: business, units, currency, notifications, subscription, sync diagnostics | Same + assignment prefs |

Hide staff complexity until needed.

---

## UI patterns (mobile)

- Bottom nav 5 + FAB New Order  
- Lists → detail → sheets for quick edits  
- Skeletons on load; toast for local save  
- Destructive actions behind confirm  

Realistic sample row:

> ORD-1042 · Efua Mensah · 2× dress · Sewing · Due Fri · GH₵200 of 800 paid

**Slice 5 copy (code):** status "Recorded on this device" until synced, then none; "Voided" when voided; method line "{Method} payment recorded · date"; toast "{Method} payment recorded on this device"; balance "GH₵ 600.00 left" / "Paid in full" / "GH₵ X overpaid"; summary "GH₵ 200.00 of GH₵ 800.00 paid". Amounts always GH₵ with 2 decimals, en-GH.

**Payment input (code):** accepts "300", "300.5", "10,50" (decimal comma), en-GH grouping "1,000"/"1,250.50" and a GH₵/GH¢/GHS prefix; refuses "1.000" (ambiguous), minus and more than 2 decimals (never rounds). Receipt share text never includes the internal note or the customer phone. Cancelled order balance: "Order cancelled · GH₵ X recorded". Payments tab footer: "Showing the latest 100 payments. Open an order to see all of its payments." Sync details: payment conflicts read "Restore order" / "Keep both" / "Use theirs".
