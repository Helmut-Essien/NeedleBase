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

- **BusinessId** on every tenant-owned entity and every sync operation. Never query without business scope.
- **ULID string IDs** (`Ulid.NewUlid().ToString()`).
- Domain/Application own the model — Api/Mobile/Web must not invent parallel entity classes.
- Status/role values live in constants or enums in Domain/Application — do not scatter magic strings.
- Financial records (Payment) require explicit conflict resolution — never silent overwrite on sync.
- Solo businesses must work without Staff setup.
- **Contracts for speed** — Prefer slim list DTOs vs rich detail DTOs; include only fields needed for the screen/API; use `record` types; keep sync payloads minimal (ids + changed fields + `DateModified` UTC).
- **Inspect before coding** — Follow [engineering-review](../needlebase-architecture/engineering-review.md): inspect existing model/callers first; fix root cause; do not leave duplicate entity/DTO versions.

## Core entities (MVP)

| Entity | Purpose | Notes |
|--------|---------|-------|
| `Business` | Workspace | Name, phone, currency default GHS, units |
| `User` / membership | Identity + `StaffMembership` (UserId, BusinessId, Role) | Owner / Manager / Tailor |
| `Customer` | Client | Name, phone, notes, optional photo ref |
| `MeasurementProfile` | Measurements | CustomerId, garment template, fields JSON or columns, unit, dated history |
| `Order` | Sewing job | CustomerId, ref, status, due/fitting dates, assignee, totals |
| `OrderLine` | Garment lines | Template, qty, style notes/images, measurement snapshot |
| `Payment` | Money in | OrderId, amount, method, note, recordedAt, sync metadata |
| `Invitation` | Staff invite | Email/phone, role, business, expiry |
| `DeletedItem` | Sync tombstone | EntityType, EntityId, BusinessId, DeletedAt |
| Mobile-only: `SyncQueueItem`, `DeviceInfo` | Offline queue | In Mobile.Data only |

### Order status

`New → Cutting → Sewing → Fitting → Ready → Collected`  
Also: `Cancelled`. **Overdue** = derived (due date passed and not Collected/Cancelled) — badge, not necessarily stored.

### Roles (plain language)

| Role | Capabilities (MVP default) |
|------|----------------------------|
| Owner | Full control including subscription & staff |
| Manager | Operations, assign work, payments; no billing ownership unless granted |
| Tailor | Assigned orders/customers; update production; no staff/subscription; payments hidden by default |

### Payment methods (extensible strings/constants)

Cash, MoMo, Bank, Other — plus future online provider with separate confirmation flag.

## Application contracts

Place DTOs as **records** under `Application/Contracts` (or `Contracts/DTOs`):

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

## Add-entity checklist

1. Domain entity + invariants  
2. EF config in Infrastructure (+ migration)  
3. DTOs + use case  
4. Api endpoint authorized + BusinessId filter  
5. If mobile-offline: Mobile.Data mapping, sync queue ops, pull apply with `SuppressSyncQueue`, DeletedItem type  
6. Web pages if oversight needed  
7. Update [needlebase-uiux](../needlebase-uiux/SKILL.md) only if UX changes  

## Reference sample data

Business: Ama Stitches, Accra. Customer: Efua Mensah, +233…. Currency: GH₵. See UI journeys skill for flows.
