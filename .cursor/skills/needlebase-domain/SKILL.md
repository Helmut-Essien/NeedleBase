---
name: needlebase-domain
description: >-
  NeedleBase domain model, Business tenancy, roles, order/payment statuses, and
  Application contracts (DTOs). Use when adding entities, changing schemas,
  defining permissions, shared DTOs, or sync payload shapes for customers,
  measurements, orders, production, payments, or staff.
---

# NeedleBase Domain & Contracts

Core workflow: Customer → Measurements → Order → Production → Payment → Collection.

## Hard rules

- **BusinessId** on every tenant-owned entity and every sync operation, with a fail-closed global filter ([clean-architecture](../needlebase-architecture/clean-architecture.md#tenancy-fail-closed)).
- **ULID string IDs** (`IdGenerator.NewId()` / `Ulid.NewUlid().ToString()` via NUlid).
- Domain/Application own the model — Api/Mobile/Web must not invent parallel entity classes.
- Status/role values live in constants or enums in Domain/Application — do not scatter magic strings.
- Financial records (Payment) require explicit conflict resolution — never silent overwrite on sync.
- Solo businesses must work without Staff setup.
- **Contracts for speed** — Prefer slim list DTOs vs rich detail DTOs; include only fields needed for the screen/API; use `record` types; keep sync payloads minimal (ids + changed fields + `DateModified` UTC).
- **Inspect before coding** — Follow [engineering-review](../needlebase-architecture/engineering-review.md): inspect existing model/callers first; fix root cause; do not leave duplicate entity/DTO versions.

## Core entities (MVP)

| Entity | Purpose | Notes | Status |
|--------|---------|-------|--------|
| `Business` | Workspace | Name, phone, `CurrencyCode` "GHS", `MeasurementUnit` "cm" | **Built** |
| `StaffMembership` | User ↔ Business + Role | UserId, BusinessId, Role (`BusinessRole`), IsActive | **Built** (tenant-filtered) |
| `Customer` | Client | `Name`, `Phone`, `Notes`, `PhotoRef`, `CreatedAtUtc`, `DateModifiedUtc` | **Built** (tenant-filtered) |
| `MeasurementProfile` | Measurements | CustomerId, `GarmentTemplate`, `Unit`, `FieldsJson`, `Notes`, `MeasuredAtUtc`, `DateModifiedUtc` | **Built** (tenant-filtered) |
| `Order` / `OrderLine` | Sewing job / garment lines | ref, status, due/fitting dates, assignee, totals; line template, qty, measurement snapshot | **Built** (S3) |
| `Payment` | Money in | OrderId, amount, method, note, recordedAt, sync metadata | Planned S5 |
| `DeletedItem` | Sync tombstone | EntityType, EntityId, BusinessId, DeletedAt | Built S4 (`Domain/Entities/DeletedItem.cs`; server-only, written in `ApplicationDbContext.SaveChanges`) |
| `Invitation` | Staff invite | Email/phone, role, business, expiry | Planned S7 |
| Mobile-only `SyncQueueItem`, `DeviceInfo` | Offline queue | Mobile.Data only | Planned S4 |

Users are ASP.NET Identity users (Infrastructure); the domain sees `UserId` strings.

### Order status

`New → Cutting → Sewing → Fitting → Ready → Collected`  
Also: `Cancelled`. **Overdue** = derived (due date passed and not Collected/Cancelled) — badge, not necessarily stored.  
Status constants: `OrderStatus` in `Domain/Enums` (same static-class pattern as `BusinessRole`). UI display names/colours/icons: [content-and-states.md §6](../needlebase-uiux/content-and-states.md#6-status-names--colours-single-source) (`Ready` displays as "Ready to collect").

### Order rules [code]

- **Total:** `Order.TotalAmount` is always the rounded sum of line totals (computed in `ReplaceLines`). Clients may send `TotalAmount`; a mismatch is rejected (`ORDER_TOTAL_MISMATCH`), never silently overwritten. Lines are immutable after create, so `UpdateOrderRequest` has no total. **Discounts/deposits are planned** (not in code; add an explicit discount field, never a hand-typed total).
- **Line ids** are always generated server-side (`OrderLineFactory.Build` → `IdGenerator.NewId()`); `CreateOrderLineRequest` has no `Id`. Idempotency is per order id.
- **DisplayRef:** 8 upper-case chars (`FieldLimits.OrderDisplayRef`), default = last 8 of the ULID; unique per business (`(BusinessId, DisplayRef)` unique index, API + mobile). On collision, retry with `Order.NewRandomDisplayRef()` (Crockford base32).
- **Rule errors:** `Domain/Exceptions/DomainRuleException` (an `ArgumentException`) carries a `Code` from `DomainErrorCodes` (`ORDER_LOCKED` for edits/status changes on Collected/Cancelled, `ORDER_STATUS_INVALID`). Callers map codes, **never** exception message text (`OrderLineFactory.FromException`).
- **Assignee** must be an active `StaffMembership` of the business (`ASSIGNEE_INVALID`).
- **"Today"** (overdue, due-today) uses `Application/Common/BusinessTime.Today(utcNow)` (Africa/Accra).

Built constants: `BusinessRole` (Owner/Manager/Tailor), `GarmentTemplate` (+ `DisplayName`), `MeasurementUnit` (`cm`/`in`), `OrderStatus`, `Common/FieldLimits`.

### Roles (plain language)

| Role | Capabilities (MVP default) |
|------|----------------------------|
| Owner | Full control including subscription & staff |
| Manager | Operations, assign work, payments; no billing ownership unless granted |
| Tailor | Assigned orders/customers; update production; no staff/subscription; payments hidden by default |

### Payment methods (extensible strings/constants)

Cash, MoMo, Bank, Other — plus future online provider with separate confirmation flag.

## Amount rules & snapshots (second audit)

- Input rules live in Application (`AmountRules`, `DecimalInput`), not the domain (pulled/old data must still load): unit price > 0, ≤ 2 decimals, ≤ GH₵ 1,000,000; ≤ 50 lines; measurements > 0 and ≤ 300 cm / 118 in (same physical cap). Sync push is lenient (skips these limits) so queued/legacy data never fails; an edit re-checks only changed fields.
- `DecimalInput.TryParse`: one '.' or ','; refuses thousands-looking input ("1,000", "1.000", "12,500") except a zero integer part ("0.125").
- Order lines **snapshot** measurement values. If the referenced profile is gone but the line carries fields, the line keeps its snapshot with `MeasurementProfileId = null`; with no fields → `MEASUREMENT_NOT_FOUND`.
- Search keys: `CustomerSearchKeys.NameKey` folds accents (FormD, ɛ→e, ɔ→o, ŋ→n) and splits on hyphens/apostrophes; `WordPrefixMatch` returns the range in the original string. Phone terms of 4+ digits also match anywhere (non-Ghana numbers).

## Application contracts

Place DTOs as **records** under `Application/Contracts/<Area>/` (today: `Auth/`, `Customers/`, `Measurements/`):

- Auth: login, refresh, register business, accept invite  
- CRUD Read/Create/Update/Patch per aggregate as needed  
- Sync: `SyncPushRequest`, `SyncPushResult`, `SyncPullRequest`, `SyncPullResult` (align mobile + api)  
- Include `BusinessId` and `DateModified` (UTC) on synced payloads  

**Do not** duplicate DTOs in Api or Mobile projects.

## Tenancy & auth claims

JWT (or equivalent) should carry at least:

- `sub` (user id)  
- `business_id` (active workspace)  
- `role` (in that business)  

Switching business (if multi-business later) switches claim + mobile local scope.

## Add-entity checklist (single source)

1. Domain entity + invariants (+ status/role constants in `Domain/Enums`); field limits in `Common/FieldLimits`.
2. EF config in Infrastructure: `BusinessId` index, **fail-closed** query filter, migration.
3. Application DTOs (list vs detail records) + use-case service in Application.
4. Api controller: policy-authorised, BusinessId-scoped, paginated lists, errors via `ApiProblems` with new codes.
5. Tests: cross-tenant isolation, idempotent create (client ULID), ProblemDetails 404/409 ([testing.md](../needlebase-architecture/testing.md)).
6. Mobile offline: Mobile.Data mapping + local service; (S4+) queue CREATE/PATCH/DELETE, pull apply with `SuppressSyncQueue`, `DeletedItem` type constant, pending count to SyncChip, test "offline create → reopen → sync once".
7. Mobile UI if on the daily path; Web pages if oversight is needed.
8. Field limits identical across Domain, EF, DTO validation and MAUI validation in the same change.
9. Update [needlebase-uiux](../needlebase-uiux/SKILL.md) only if UX changes.

## Reference sample data

Business: Ama Stitches, Accra. Customer: Efua Mensah, +233…. Currency: GH₵. See UI journeys skill for flows.

## Phones (third audit)
- `Domain/Common/PhoneNumbers`: `Normalize` is applied by `Customer` on every create/update (Ghana numbers stored as `+233XXXXXXXXX`); `Check` is the strict format rule (9–15 digits; Ghana exactly 9 national digits) used by REST/UI for new or changed numbers only; `Same` compares numbers regardless of formatting. Changing `Normalize` needs a server backfill and a device schema step.


## Payment (Slice 5)
`Payment` is immutable: Amount (>0, ≤ 50,000,000, max 2 decimals, never rounded), Method (`PaymentMethod` Cash/MoMo/Bank/Other), Note, RecordedAtUtc, OrderId, CustomerId. Only change: `Void(reason, now)` — one-way, idempotent, first reason wins. Balance = order total − sum of non-voided payments (`PaymentRules.Paid`). Entry rules in `PaymentRules.CheckEntry` (strict for REST/device, lenient for sync push).

## AuditEvent (22 Oct 2026)

Append-only record: business, actor + role snapshot, action (`entity.verb`, see `AuditActions`), entity type/id, order/customer links, summary (≤300), masked changes JSON, source (api/web/sync/system), device, IP, UA, correlation id, UTC time. No FKs (survives deletes). Never edited; purged after `Audit:RetentionDays` (730).
