# Mobile Key Screens

Primary daily tool for tailors. Touch-first, offline-capable, uncluttered for busy shop floors.

Sample business: **Ama Stitches**, Accra. Currency **GH₵**.

---

## Onboarding & authentication

| Screen | Content | Notes |
|--------|---------|-------|
| Welcome | Logo, short promise (“Manage orders & measurements — works offline”), Log in / Create account | Brand-first, no metric clutter |
| Sign up | Name, phone/email, password | Clear validation |
| Login | Phone/email + password; Forgot password | |
| Password recovery | Phone/email → instructions | |
| Business profile | Business name, phone, location (optional), logo optional | Solo-friendly; no staff required |
| Offline explainer | One screen: local save + sync later | Mandatory once |
| Trial / activation | Plan summary + Start | Soft; then Home |
| Accept invite | Shop name + role explanation → Join | |

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

Avoid vanity KPIs. Strong hierarchy; few cards.

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

Statuses: New → Cutting → Sewing → Fitting → Ready → Collected (+ Cancelled, Overdue flag).

---

## Payments & receipts

- Customer / order balances  
- Record payment: amount, method (Cash, MoMo, Bank, Other), order link, note  
- History list with sync badges on pending  
- Receipt preview → Share / Print  
- Distinguish local cash vs any online provider (see offline-sync)

Default currency GH₵.

---

## Staff & settings (More)

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
