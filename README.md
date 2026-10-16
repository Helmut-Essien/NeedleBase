# NeedleBase

Offline-first **tailoring business management** for HelmutCode. Built for Ghanaian / West African workshops: register customers, take measurements, sew orders, track production, collect payments — on a phone that works without reliable internet, with a web console for owners and managers.

**Stack:** .NET 9 · ASP.NET Core API · Razor Pages web · .NET MAUI (Android) + SQLite · Clean Architecture · ULID string IDs (NUlid)

---

## What the application does

NeedleBase is the operating system for a sewing / tailoring shop.

A **Business** (e.g. *Ama Stitches, Accra*) is an isolated workspace. The owner signs up, then staff (optional) join by invite. Day to day, tailors and managers:

1. **Register customers** — name, phone, notes (sample: *Efua Mensah, +233…*).
2. **Record measurements** — garment templates (dress, shirt, etc.), units, dated history per customer.
3. **Create sewing orders** — one or more garment lines, each holding a **snapshot** of measurements at order time so later edits to the customer profile do not rewrite the job.
4. **Move work through production** — status pipeline from new job → cutting → sewing → fitting → ready → collected (or cancelled). Overdue is derived when the due date has passed and the order is not finished.
5. **Collect money** — deposits and further payments (Cash, MoMo, Bank, Other); outstanding balance = order total − sum(payments); receipts for share/print.
6. **Stay honest offline** — mobile saves to SQLite first (“Saved on this device”), queues changes, then syncs to the cloud when online without duplicating records.
7. **Oversee from the web** — owners/managers see dashboard and a production board (status columns, assignees) fed only via the API.

**Who uses it**

| Role | What they do |
|------|----------------|
| **Owner** | Full control: business settings, staff, subscription, payments, all orders |
| **Manager** | Operations: customers, orders, assign work, payments; not billing ownership unless granted |
| **Tailor** | Assigned work and related customers; update production; **no** Staff/Subscription; Payments hidden by default |

A solo tailor can run the whole shop as Owner with **zero** staff setup.

### Platforms

| Platform | What the user experiences |
|----------|---------------------------|
| **Mobile (MAUI)** | Bottom nav: Home · Customers · Orders · Payments · More (+ FAB **New Order**). Primary daily tool; full offline MVP. |
| **Web** | Left sidebar oversight: Dashboard, Customers, Measurements, Orders, Production, Payments, Staff, Settings. Online only via API. |
| **Api + PostgreSQL** | Auth, tenancy, sync endpoints, CRUD; JWT + refresh; claims `sub`, `business_id`, `role`. |

### Priority journeys (acceptance targets)

| # | Journey | Outcome |
|---|---------|---------|
| 1 | Register business → customer → measurements → order → deposit → status | End-to-end happy path (mobile primary) |
| 2 | Create order offline → reopen → sync | Data persists; **one** order after sync (no duplicates) |
| 3 | Update status / assign staff → see on web | Production board + dashboard reflect mobile changes |
| 4 | Additional payment → balance → receipt | Outstanding updates; receipt works offline with pending note |
| 5 | Invite employee as Tailor | Limited nav and permissions |

---

## Domain model (entities)

All tenant-owned rows carry **`BusinessId`**. Primary keys are **ULID strings** (`IdGenerator` / NUlid), assigned on the client when offline so sync is idempotent.

```
Business
  └── StaffMembership (User + Role) ← Invitation
  └── Customer
        └── MeasurementProfile
        └── Order
              ├── OrderLine  (measurement snapshot, style notes)
              └── Payment
DeletedItem          (server tombstones for mobile pull)
SyncQueueItem / DeviceInfo   (mobile SQLite only)
```

| Entity | Purpose | Key fields / notes |
|--------|---------|-------------------|
| **Business** | Workspace / shop | Name, phone, default currency GHS, units |
| **User** + **StaffMembership** | Identity in a business | Role: Owner / Manager / Tailor |
| **Invitation** | Staff onboarding | Email/phone, role, expiry; accept joins membership |
| **Customer** | Client of the shop | Name, phone, notes, optional photo |
| **MeasurementProfile** | Body / garment measures | CustomerId, garment template, fields, unit, dated history |
| **Order** | One sewing job | CustomerId, readable/ULID ref, status, due/fitting dates, assignee, totals |
| **OrderLine** | Garment within an order | Template, qty, style notes/images, **measurement snapshot** |
| **Payment** | Money received | OrderId, amount, method, note, recordedAt; conflict-safe on sync |
| **DeletedItem** | Sync tombstone | EntityType, EntityId, BusinessId, DeletedAt |
| **SyncQueueItem** | Mobile outbound queue | Op type, payload, status — Mobile.Data only |
| **DeviceInfo** | Device sync metadata | LastSync, device id — Mobile.Data only |

### Order status

```
New → Cutting → Sewing → Fitting → Ready → Collected
                                    ↘ Cancelled
```

**Overdue** = due date passed and status is not Collected/Cancelled (badge/derived, not a stored status).

### Payments

- Methods: **Cash**, **MoMo**, **Bank**, **Other** (extensible).
- Balance: `order total − Σ(payments)`.
- Offline payments are local + pending sync; UI must not imply an external provider confirmed them.
- Sync conflicts on payments are **never silently overwritten** — API returns a conflict payload; UI resolves.

### Offline sync behaviour

- Saves go to SQLite and enqueue unless pull apply sets `SuppressSyncQueue`.
- Push + pull since `LastSync`; apply deletes via `DeletedItem`.
- SyncChip states: Offline · Waiting to sync · Syncing · Synced · Failed.
- Transient network/refresh failure does **not** clear tokens or empty the queue.

### Hard product rules

- Local save ≠ cloud sync — copy like “Saved on this device”
- Solo businesses work with zero staff
- Default money: **en-GH / GH₵ (GHS)**
- Never invent multi-currency or multi-business switching until requested

### Explicitly out of scope (until asked)

Multi-currency runtime, multi-business switching UI, full web offline parity, payment-provider webhooks as source of truth, AI features, Redis, Mediator “for later”, Excel/PDF reporting suites.

---

## Implementation plan (slices)

Deliver **one slice at a time**; confirm before starting the next. Detail and acceptance: `.cursor/skills/needlebase-phases/phases.md`.

| Slice | Status | What gets implemented |
|-------|--------|------------------------|
| **0** Scaffold | **Done** | Solution, Clean Architecture projects, health endpoints, README, package baselines, Web/Mobile shells |
| **1** Auth + Business | **Done** | Register business + owner; login; JWT + refresh; `business_id` / `role` claims; mobile secure token store + session; Web cookie/session + HttpClients; BusinessId global query filters |
| **2** Customers + measurements | **Done** | Domain + Api CRUD (paginated, Business-scoped); mobile Customers tab + measurement capture; local SQLite save. Web customers deferred to Slice 6 (oversight). |
| **3** Orders + production | Planned | Orders/lines; status pipeline; mobile New Order wizard + Orders tab; Api list/detail |
| **4** Offline sync | Planned | Sync queue, push/pull, `DeletedItem`, SyncChip / Sync details, idempotent ULIDs, token honesty under offline |
| **5** Payments | Planned | Record deposits/payments, balances, receipts; conflict-safe sync + UI resolution |
| **6** Web oversight | Planned | Sidebar shell; Dashboard; Production board; Orders via Api HTTP |
| **7** Staff + roles | Planned | Invitations, role policies, role-filtered mobile/web nav |
| **8** Subscription + polish | Planned | Trial/subscription UI; empty/error/recovery; indexes; smoke tests (auth, tenancy, sync, payment conflicts) |

---

## Solution layout

```
NeedleBase.sln
├── src/
│   ├── NeedleBase.Domain/           # Entities, enums — no infra deps
│   ├── NeedleBase.Application/      # Use cases, contracts/DTOs, NUlid IDs
│   ├── NeedleBase.Infrastructure/   # EF PostgreSQL, Identity (from Slice 1)
│   ├── NeedleBase.Api/              # ASP.NET Core host
│   ├── NeedleBase.Web/              # Razor Pages — HTTP to Api only (no SQL)
│   ├── NeedleBase.Mobile/           # .NET MAUI (MVVM)
│   └── NeedleBase.Mobile.Data/      # SQLite LocalDbContext (customers/measurements; sync queue Slice 4)
└── tests/
    ├── NeedleBase.Domain.Tests/
    ├── NeedleBase.Application.Tests/
    ├── NeedleBase.Api.Tests/
    └── NeedleBase.Mobile.Tests/
```

**Dependency rule:** Domain ← Application ← Infrastructure; Api composes Application + Infrastructure; Web → Application contracts + HTTP to Api (never DbContext); Mobile → Mobile.Data + Application (never Infrastructure/Api project refs).

## Package baselines

| Area | Package | Version target |
|------|---------|----------------|
| IDs | NUlid (`Ulid.NewUlid().ToString()`) | 1.7.3 |
| API EF | Npgsql.EntityFrameworkCore.PostgreSQL | 9.0.4 |
| Mobile EF | Microsoft.EntityFrameworkCore.Sqlite | 9.0.9 |
| MVVM | CommunityToolkit.Mvvm / CommunityToolkit.Maui | 8.4.0 / 11.2.0 |

Do **not** add Mediator, Redis, or other infra “for later” without a measured need.

## Prerequisites

- .NET 9 SDK
- For mobile: `maui-android` workload (`dotnet workload install maui-android`)
- PostgreSQL (local or Docker) — required from Slice 1 onward

## Restore / build / test

```bash
dotnet restore NeedleBase.sln
dotnet build NeedleBase.sln
dotnet test NeedleBase.sln
```

## Run Api + Web (separate processes)

```bash
dotnet run --project src/NeedleBase.Api --launch-profile https
dotnet run --project src/NeedleBase.Web --launch-profile https
```

| App | HTTPS (dev) | Health |
|-----|-------------|--------|
| Api | https://localhost:7079 | `/health/live`, `/health/ready` (DB) |
| Web | https://localhost:7049 | Home page probes Api `/health/live` |

Mobile ping: `GET /api/test/ping`

### Secrets (never commit)

| Setting | Where | Notes |
|---------|--------|--------|
| `ConnectionStrings:DefaultConnection` | Api user secrets / env | Empty in committed appsettings; never commit passwords |
| `JwtSettings:SecurityKey` | Api user secrets / env | Min 32 characters; required at startup; never commit |
| `Api:BaseUrl` | Web `appsettings.Development.json` or user secrets | Web **fails startup** if missing |
| `FrontendBaseUrl` | Api config | Used for email links later — no hardcoded hosts |

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=NeedleBase;Username=postgres;Password=..." --project src/NeedleBase.Api
dotnet user-secrets set "JwtSettings:SecurityKey" "your-long-dev-security-key-here!!!!" --project src/NeedleBase.Api
dotnet user-secrets set "Api:BaseUrl" "https://localhost:7079" --project src/NeedleBase.Web
```

Environment variables also work (`ConnectionStrings__DefaultConnection`, `JwtSettings__SecurityKey`, `Api__BaseUrl`).

### Auth endpoints (Slice 1)

| Method | Path | Notes |
|--------|------|--------|
| POST | `/api/auth/register-business` | Creates Business + Owner; returns tokens + user |
| POST | `/api/auth/login` | Returns tokens + user (`business_id`, `role`) |
| POST | `/api/auth/refresh-token` | Rotates refresh token |
| GET | `/api/me` | Requires JWT + `business_id` + staff role |

Mobile may send `X-Auth-Token: Bearer …` instead of `Authorization` (mapped on the Api).

**BusinessId tenancy:** `ApplicationDbContext` uses **global query filters** on `IBusinessScoped` entities when `CurrentBusinessId` is set from JWT by `BusinessTenancyMiddleware`. Auth/bootstrap paths clear the filter by leaving it null.

**Migrations:** Development auto-applies `dotnet ef` migrations on Api startup. Prefer explicit `dotnet ef database update` in CI/Production.
