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
| `Order` / `OrderLine` | Sewing job / garment lines | ref, status, due/fitting dates, assignee, totals; line template, qty, measurement snapshot | Planned S3 |
| `Payment` | Money in | OrderId, amount, method, note, recordedAt, sync metadata | Planned S5 |
| `DeletedItem` | Sync tombstone | EntityType, EntityId, BusinessId, DeletedAt | Planned S4 |
| `Invitation` | Staff invite | Email/phone, role, business, expiry | Planned S7 |
| Mobile-only `SyncQueueItem`, `DeviceInfo` | Offline queue | Mobile.Data only | Planned S4 |

Users are ASP.NET Identity users (Infrastructure); the domain sees `UserId` strings.

### Order status

`New → Cutting → Sewing → Fitting → Ready → Collected`  
Also: `Cancelled`. **Overdue** = derived (due date passed and not Collected/Cancelled) — badge, not necessarily stored.  
Status constants: planned S3 (follow the `BusinessRole` static-class pattern in `Domain/Enums`). UI display names/colours/icons: [content-and-states.md §6](../needlebase-uiux/content-and-states.md#6-status-names--colours-single-source) (`Ready` displays as "Ready to collect").

Built constants: `BusinessRole` (Owner/Manager/Tailor), `GarmentTemplate` (+ `DisplayName`), `MeasurementUnit` (`cm`/`in`), `Common/FieldLimits`.

### Roles (plain language)

| Role | Capabilities (MVP default) |
|------|----------------------------|
| Owner | Full control including subscription & staff |
| Manager | Operations, assign work, payments; no billing ownership unless granted |
| Tailor | Assigned orders/customers; update production; no staff/subscription; payments hidden by default |

### Payment methods (extensible strings/constants)

Cash, MoMo, Bank, Other — plus future online provider with separate confirmation flag.

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
