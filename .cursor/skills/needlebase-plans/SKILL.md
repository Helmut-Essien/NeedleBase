---
name: needlebase-plans
description: >-
  Decided NeedleBase pricing, plan limits, PlanCatalog rules, and the
  implementation order. PlanCatalog (step 1) and staff-limit enforcement
  (step 2) are built. Free mode and later steps are not built. The
  published Terms and the landing-page prices were updated on
  18 November 2026. Use when talking about Free, Starter, Workshop or
  Production, enforcing plan limits, upgrading a shop, or how licence
  plans from Platform are applied. Do not start the next build step
  until the user asks.
---

# NeedleBase plans

**Status: decided.** PlanCatalog (step 1) and staff-limit enforcement (step 2) are built. Free mode and later steps are **not built** — do not start them until the user asks. The published Terms and the public landing page already show these prices (updated 18 November 2026). Do not change those public prices or the published Terms again unless the user asks.

Purpose: the decided prices, limits, and how NeedleBase will apply a plan name from Platform.

Follow [standing-rules.md](../standing-rules.md) on every change in this repo.

A short price table also lives in the README section [Pricing (decided, not built)](../../../README.md#pricing-decided-not-built). This skill is the full plan, including PlanCatalog rules, the build order, risks, and how it works with Platform.

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

Do not start the next build step until the user asks. Steps 1–6 are enough to launch Free and Starter. Do not sell Workshop or Production until step 7 works. Step 8 is done for the Terms and the landing prices (18 November 2026); the Play Store privacy form is still pending.

| # | Step | Size | What to build |
|---|------|------|----------------|
| 1 | PlanCatalog | S | **Done (25 Nov 2026).** `Application/Licensing/PlanCatalog.cs` maps `starter` / `workshop` / `production` to staff limits and features. Keyless and complimentary → unlimited; trial → Starter; unknown/blank on a keyed shop → Starter (`WasUnknownPlan` for logging). Tests: `Application.Tests/PlanCatalogTests`. |
| 2 | Staff limits on the server | M | **Done (25 Nov 2026).** `StaffPlanLimits` + `STAFF_LIMIT` on invite, accept-invite and reactivate (Owner not counted; pending invites reserve seats; business row lock). Downgrade keeps existing staff and blocks new seats. Keyless/complimentary stay unlimited. Read-only lock unchanged. Tests: `StaffPlanLimitsTests`, `StaffApiTests.PlanLimits`. |
| 3 | Free mode on the phone | L | The first screen offers "Use free on this phone" next to signing in, entering a licence key, or joining with an invite. Local counters that never count down. "Save a copy". |
| 4 | Upgrading from Free | L | The Owner enters a licence key, the phone's records move to the new online shop, and the first sync uploads them. If the app closes halfway, it picks up again safely. |
| 5 | Export and usage display | M | Paid shops and lapsed read-only shops can export. The License page shows the plan and usage. Free shows meters such as "7/10 customers". Receipts show the shop's phone number. |
| 6 | Paid emails | M | Starter: a monthly summary email to the Owner. Workshop and Production: that monthly summary, plus a weekly summary to the Owner that includes the balances owed, and emails to staff when an order is assigned or due. |
| 7 | Workshop features | L | Photos, PDF receipts and profit and monthly reports. Don't sell Workshop or Production until these work. |
| 8 | Legal, Play Store and landing page | S | **Done (18 November 2026) for the Terms and the landing prices** (published prices excluding VAT, 30-day price-change notice, Free-plan rules, export and lapse wording, and landing prices GH₵0 / 50 / 150 / 300 with quarter and year figures). **Still pending:** the Play Store privacy form. |

## Risks

- Until the NeedleBase product in Platform has the `starter` / `workshop` / `production` plans, and any old names such as "Pro" are mapped onto them, NeedleBase must fail closed to Starter for a blank or unknown plan, not unlimited. Platform already has the remap tool; the admin still has to add those plans and map old names.
- Platform's server default for `MaxDevices` is still 3. Set NeedleBase's `DefaultMaxDevices` to **1** (one shop per key) in Platform's admin. Until that is set, new NeedleBase keys get 3.
- A plan change on a live key is picked up on the next re-check (within a day, or sooner if the Owner re-enters the key). Edit the same key's plan; do not revoke and issue a new one, which would disconnect the shop.
- The published Terms and the landing page now promise export, the Free "Save a copy" action, and Workshop and Production features (photos, PDF receipts, and profit and monthly reports) that are not built yet. Do not sell Workshop or Production until step 7 works. Do not change the public landing-page prices or the published Terms again unless the user asks.

## Related

| Need | Where |
|------|-------|
| Standing rules | [standing-rules.md](../standing-rules.md) |
| Slice status and later work | [needlebase-phases](../needlebase-phases/SKILL.md) |
| How shops are licensed today | [docs/platform-licensing.md](../../../docs/platform-licensing.md) |
| Legal notes (Terms and landing prices updated 18 November 2026; Play Store privacy form still pending) | [docs/legal/README.md](../../../docs/legal/README.md) |
| Platform product plans (other repo) | `/home/helmut/Documents/My Projects/platform/.cursor/skills/platform-license-hub/product-plans.md` |
