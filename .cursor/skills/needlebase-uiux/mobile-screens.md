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

**Implemented Home [code, from Figma *Mobile — Home*]:** header (initials + greeting + business name; **no notifications bell** — removed until a real screen exists), a **SyncChip** (Slice 4: honest sync state, opens Sync details), KPI cards **Active orders** and **Ready to collect** (`ActiveOrdersCount`, `ReadyToCollectCount`), 4 quick actions (Add customer · New order · Payment · Measure), **Orders in progress** by stage + View all, and **Needs attention** with the empty state "Nothing needs attention right now". Both KPIs pass the rule once Orders exist (Slice 3). Until then they honestly show 0 and must link to the Orders list when it ships. Open item: the "Offline-ready" card is replaced by the SyncChip in Slice 4.

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

---

## Payments & receipts

**[planned Slice 5]**: methods from domain (Cash, MoMo, Bank, Other). NeedleBase *records* payments; it doesn't process MoMo (Terms §5.1), so copy must never say "paid via MoMo" as if confirmed by the network. Use "MoMo payment recorded".

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
