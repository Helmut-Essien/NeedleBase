---
name: needlebase-plans
description: >-
  Decided NeedleBase pricing, plan limits, PlanCatalog rules, and the
  implementation order. Steps 1–6 are built. Step 7 is in progress:
  PDF receipts and customer email (with PDF receipt attachment) are
  built; profit/monthly reports built (26 Nov 2026); expenses
  (step 7b-2) in progress — Domain+EF+Api+reports/export/email+server
  sync + Mobile.Data done (chunks 1–6; expenses-plan.md); phone/web UI remain; then photos via
  Cloudflare R2 (not started). Privacy/Terms updated 26 November 2026 for customer
  email. Use when talking about Free, Starter, Workshop or Production,
  enforcing plan limits, upgrading a shop, or how licence plans from
  Platform are applied. Do not start a new plan step until the user asks.
---

# NeedleBase plans

**Status: decided.** PlanCatalog (step 1), staff-limit enforcement (step 2), Free phone-only mode (step 3), upgrading from Free (step 4), export/usage display (step 5) and paid emails (step 6) are built. **Step 7 (Workshop features) is in progress:** PDF receipts and **customer email channel** (status + receipt with PDF attachment, opt-in) are built; **profit and monthly reports** are built (26 Nov 2026); **expenses (step 7b-2) in progress (27 Nov 2026)** — Domain + EF + Api + reports/export/owner-email + server sync + Mobile.Data (chunks 1–6) done; phone/web UI remain — single source [expenses-plan.md](expenses-plan.md); **customer photos (step 7c)** follow and will use **Cloudflare R2** (not started). Privacy/Terms describe customer email (updated 26 November 2026). Do not start photos or a later plan step until the user asks. The public landing page shows the decided prices (updated 18 November 2026). Do not change those public prices or the published Terms again unless the user asks.

Purpose: the decided prices, limits, and how NeedleBase will apply a plan name from Platform.

Follow [standing-rules.md](../standing-rules.md) on every change in this repo.

A short price table also lives in the README section [Pricing (decided; Free + staff caps built)](../../../README.md#pricing-decided-free--staff-caps-built). This skill is the full plan, including PlanCatalog rules, the build order, risks, and how it works with Platform.

## How it works with Platform

Platform is the source of truth for **which plan a shop is on**. NeedleBase only *reads* Platform: it already calls `POST /api/licenses/validate` and stores `Business.LicensePlan` from the signed token's `plan` claim, plus trial and complimentary flags and expiry. That validate contract does not need to change.

Platform already has per-product plan names. They were built and merged to `master` in PR #11 (`9697838`): a `ProductPlan` catalog, a dropdown when issuing or editing a key, a check that the key uses one of the product's active plans, a way to map old names such as "Pro" onto the new codes, and `ServiceProduct.DefaultMaxDevices`. Details are in the Platform repo at `/home/helmut/Documents/My Projects/platform/.cursor/skills/platform-license-hub/product-plans.md`. This repo treats Platform as read-only. Do not change Platform from here.

The admin still has to add the `starter`, `workshop` and `production` plans to the NeedleBase product in Platform, and set that product's default device limit to 1. Until the plans are added, NeedleBase keys can still use free-text plan names.

NeedleBase's job is **what each plan allows**. That lives in a `PlanCatalog` in Application, not in Platform.

Free never touches Platform: it has no account and no licence key.

## Tiers and prices

Prices are in Ghana cedis and **do not include VAT**. A year costs 10 months. There is no free trial of paid features and no founding-shop discount. A referral offer is planned. Starter's monthly price is a launch price and may rise later; existing shops move to the new price after 30 days' notice.

| Plan | Price | Staff (Owner not counted) | What it includes |
|------|-------|---------------------------|------------------|
| **Free** | GH₵0 | Owner only, on the phone | See [Free (phone only)](#free-phone-only). |
| **Starter** (`starter`) | GH₵50 / month, GH₵140 / quarter, GH₵500 / year | Owner + 2 staff | Sync, a cloud backup, the website, unlimited customers and orders, a monthly summary email to the Owner, export, and the shop's phone number on receipts. |
| **Workshop** (`workshop`) | GH₵150 / month, GH₵430 / quarter, GH₵1,500 / year | Owner + 8 staff | Everything in Starter (including the monthly summary email), plus assignment and due-date emails, a weekly summary email to the Owner that includes the balances owed, photos, PDF receipts, and profit and monthly reports. |
| **Production** (`production`) | GH₵300 / month, GH₵850 / quarter, GH₵3,000 / year | Owner + 20 staff | Everything in Workshop, plus more photo storage and a setup call. |

Staff limits exclude the Owner. Complimentary keys are unlimited (the stored plan is ignored for limits). When a paid shop stops paying, the existing read-only lock applies and the shop can still export.

WhatsApp stays **tap-to-send** on every plan. Automatic WhatsApp template messages exist in code but are **not** a sold plan feature until Meta verifies the business. Paid-plan notifications are **email** (to the Owner and staff, not to customers).

**Customer email channel (26 Nov 2026):** customers may store an optional email (phone stays required). With a separate **email opt-in** (not the same as WhatsApp consent), NeedleBase may send transactional email when SMTP is configured:

- **Order status** updates (same statuses the shop enables for WhatsApp when that is on; otherwise at least Ready / Collected), after the change is on the server (API or sync push — so offline phones send as soon as sync succeeds).
- **Payment receipt** copy (email body matching the share receipt, plus a **PDF attachment** generated on send — not stored in blob; Share text/PDF on the phone remains).

Not bulk marketing. Privacy/Terms describe this (updated 26 November 2026); see `docs/legal/README.md`. WhatsApp tap-to-send and (when Meta-verified) automatic WhatsApp stay available.

## PlanCatalog

Built in `Application/Licensing/PlanCatalog.cs` (`PlanCodes`, `PlanEntitlements`, `PlanResolution`, `PlanCatalog.Resolve` / `ForCode`). One table maps a lowercase plan code from Platform to NeedleBase limits and features. Codes are stable: `starter`, `workshop`, `production`.

- Look up the code in lowercase (trim first).
- **Complimentary** keys (`LicenseIsComplimentary`) are unlimited, regardless of the stored plan.
- **Trial** keys (`LicenseIsTrial`) act as **Starter** until expiry, even if the key carries `workshop` or `production`. After the trial ends and Platform clears the trial flag (usually on conversion), the carried plan applies. NeedleBase does not offer a free trial of paid features; this rule is only for a trial key Platform may still issue.
- An **unknown or blank** plan on a licensed shop **fails closed to Starter**, never to unlimited. Invite/accept/reactivate log `WasUnknownPlan` via `StaffService` / `AuthService`.
- **Keyless** shops (created before licensing, or registered without a key when that is allowed) stay **unlimited**. Do not lock them to Free.
- **Free** is phone-only and never touches Platform (see below).
- On **downgrade**, keep the staff the shop already has and block new invites (and accept/reactivate) until the shop is under the new cap (`StaffPlanLimits` + `STAFF_LIMIT`).
- A **lapsed** shop is read-only (existing behaviour) and can still export.

## Free (phone only)

- No account, no licence key, no sync, no website. It never calls Platform.
- Owner only.
- 10 customers and 20 orders **ever created**. Deleting a customer or an order does **not** free a slot.
- WhatsApp by tap-to-send. "Save a copy" export only (no cloud backup).
- When the shop starts paying, the data already on the phone must upload cleanly on the first sync.

## Implementation order

Do not start a new plan step until the user asks. Steps 1–6 are enough to launch Free and Starter. Do not sell Workshop or Production until step 7 (PDF receipts, reports, and photos) works. Step 8 is done for the Terms and the landing prices (18 November 2026); the Play Store privacy form is still pending.

| # | Step | Size | What to build |
|---|------|------|----------------|
| 1 | PlanCatalog | S | **Done (25 Nov 2026).** `Application/Licensing/PlanCatalog.cs` maps `starter` / `workshop` / `production` to staff limits and features. Keyless and complimentary → unlimited; trial → Starter; unknown/blank on a keyed shop → Starter (`WasUnknownPlan` for logging). Tests: `Application.Tests/PlanCatalogTests`. |
| 2 | Staff limits on the server | M | **Done (25 Nov 2026).** `StaffPlanLimits` + `STAFF_LIMIT` on invite, accept-invite and reactivate (Owner not counted; pending invites reserve seats; business row lock). Downgrade keeps existing staff and blocks new seats. Keyless/complimentary stay unlimited. Read-only lock unchanged. Tests: `StaffPlanLimitsTests`, `StaffApiTests.PlanLimits`. |
| 3 | Free mode on the phone | L | **Done (25 Nov 2026).** Welcome offers "Use free on this phone" separate from Sign in / Register with a licence key / Join a shop with an invite (invite is for online staff only). Local ULID shop, Owner session without tokens, `FreeModeStates` lifetime counters, sync skipped via `LocalFreeModeGate`, caps on customer/order create, More → Save a copy, Free banner on Home. Tests: `FreeModeLocalServiceTests`, `FreePlanLimitsTests`. |
| 4 | Upgrading from Free | L | **Done (25 Nov 2026).** More → Go online with a licence key (`FreeUpgradePage`). Registers online shop, remaps local BusinessId (`FreeUpgradeLocalService`), clears Free gate/flags without wiping rows; adoption/first sync uploads them. `free_upgrade_pending` + `TryResumeFreeUpgradeAsync` on startup if killed mid-way. Tests: `FreeUpgradeLocalServiceTests`. |
| 5 | Export and usage display | M | **Done (25 Nov 2026).** `GET /api/export` (Owner, `AllowWhenReadOnly`) + web License download; mobile More → Export for Owner. `SubscriptionStatusDto` carries PlanCatalog display name, staff used/max, `CanExport`. Free Home/More meters (`7/10 customers · 3/20 orders`). Receipts show shop phone when session has `BusinessPhone`. |
| 6 | Paid emails | M | **Done (26 Nov 2026).** Starter: monthly Owner summary after 06:00 Accra from the 1st (`PlanEmailWorker` + `PlanEmailService`, watermarks `Business.MonthlySummarySentFor`). Workshop/Production/unlimited: that plus weekly Owner summary with balances owed (`WeeklySummarySentFor`), assignment emails on create/update/reassign (`IPlanEmailNotifier`), and day-of due emails to the assignee (`Order.DueEmailSentForDate`). Skips closed and read-only shops; SMTP off → no send. Templates in `EmailTemplates`. Tests: `PlanEmailTests`, `StaffApiTests.PlanEmails`. Landing "coming soon" notes for these emails removed. |
| 7 | Workshop features | L | **In progress (27 Nov 2026).** Build order: (a) **PDF receipts — done** (`CanPdfReceipts`, on-device PDF share). **Customer email channel — done:** optional email + separate opt-in; status emails (Ready/Collected by default, or WhatsApp-enabled statuses when WA is on) and payment-receipt emails with PDF attachment queue on the server after API/sync when SMTP is configured (`CustomerEmailOutbox`). Share text/PDF and WhatsApp remain. Privacy/Terms updated 26 Nov 2026. (b) **Profit and monthly reports — done (26 Nov 2026):** entitlement `ProfitAndMonthlyReports` → `SubscriptionStatusDto.CanReports`; `GET /api/reports/monthly?month=yyyy-MM` (Owner/Manager, `REPORTS_FORBIDDEN` for tailors, `REPORTS_NOT_ALLOWED` on Starter/trial/unknown plan, `REPORT_MONTH_INVALID`; GET so it works while read-only) via `Infrastructure/Reports/MonthlyReportService`; shared maths `Application/Reports/MonthlyReportCalculator` (Accra months; money in = non-voided payments by recorded date; orders taken = created in the month, not cancelled; still owed on those orders; payment methods; status breakdown; six-month comparison). Web `/Reports` (nav for Owner/Manager, upgrade prompt on Starter); phone More → Money → Monthly reports (`ReportsPage`, computed offline from local SQLite by `ReportLocalService`). (b-2) **Expenses — in progress (27 Nov 2026):** same entitlement (`CanReports`); chunks 1–6 done (Domain, EF, Api `/api/expenses`, calculator/export/`MonthProfitSummary` email, server sync phase 5, Mobile.Data v13/`ExpenseLocalService`/plan-hold); phone/web UI remain — [expenses-plan.md](expenses-plan.md). Report screens still show `CostsNote` until UI chunks 7–8. (c) **Photos — next, not started** — Cloudflare R2. Don't sell Workshop or Production until (a), (b), (b-2) and (c) work. |
| 8 | Legal, Play Store and landing page | S | **Done (18 November 2026) for the Terms plan prices and the landing prices** (published prices excluding VAT, 30-day price-change notice, Free-plan rules, export and lapse wording, and landing prices GH₵0 / 50 / 150 / 300 with quarter and year figures). **Customer email** described in Privacy/Terms on **26 November 2026**. **Still pending:** the Play Store privacy form. |

## Risks

- Until the NeedleBase product in Platform has the `starter` / `workshop` / `production` plans, and any old names such as "Pro" are mapped onto them, NeedleBase must fail closed to Starter for a blank or unknown plan, not unlimited. Platform already has the remap tool; the admin still has to add those plans and map old names.
- Platform's server default for `MaxDevices` is still 3. Set NeedleBase's `DefaultMaxDevices` to **1** (one shop per key) in Platform's admin. Until that is set, new NeedleBase keys get 3.
- A plan change on a live key is picked up on the next re-check (within a day, or sooner if the Owner re-enters the key). Edit the same key's plan; do not revoke and issue a new one, which would disconnect the shop.
- The published Terms and the landing page promise Workshop/Production features (photos, PDF receipts, and profit and monthly reports). PDF receipts and reports are built; expense Domain/EF/Api/report maths, server sync and Mobile.Data are built (chunks 1–6); phone/web expense and report UI are still open ([expenses-plan.md](expenses-plan.md)); photos (Cloudflare R2) are not done yet. Free "Save a copy", paid export and paid emails are built. Do not sell Workshop or Production until step 7 (a)–(c) work. Do not change the public landing-page prices or the published Terms again unless the user asks.

## Related

| Need | Where |
|------|-------|
| Standing rules | [standing-rules.md](../standing-rules.md) |
| Expenses plan (step 7b-2) | [expenses-plan.md](expenses-plan.md) |
| Slice status and later work | [needlebase-phases](../needlebase-phases/SKILL.md) |
| How shops are licensed today | [docs/platform-licensing.md](../../../docs/platform-licensing.md) |
| Legal notes (plan prices 18 Nov 2026; customer email Privacy/Terms 26 Nov 2026; Play Store privacy form still pending) | [docs/legal/README.md](../../../docs/legal/README.md) |
| Platform product plans (other repo) | `/home/helmut/Documents/My Projects/platform/.cursor/skills/platform-license-hub/product-plans.md` |
