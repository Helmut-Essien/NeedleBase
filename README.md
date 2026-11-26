# NeedleBase

NeedleBase is the operating system for tailors, seamstresses, dressmakers and fashion workshop owners. It is built for Ghanaian and West African workshops: register customers, take measurements, create orders, track production and collect payments — on a phone that works without reliable internet, with a website for owners and managers.

**Stack:** .NET 9 · ASP.NET Core API · Razor Pages web · .NET MAUI (Android) + SQLite · Clean Architecture · ULID string IDs (NUlid)

**In this README:** [What it does](#what-it-does) · [Domain model](#domain-model) · [Implementation slices](#implementation-slices) · [Solution layout](#solution-layout) · [Local development](#local-development) · [Account deletion](#account-deletion) · [Legal and licensing](#legal-and-licensing) · [Pricing (decided; Free + upgrade built)](#pricing-decided-free--upgrade-built) · [Further documentation](#further-documentation)

## What it does

A **shop** is stored as a `Business` (for example *Ama Stitches, Accra*). The Owner signs up, then staff can join by invite. Day to day, tailors and managers:

1. **Register customers** — name, phone, notes (sample: *Efua Mensah, +233…*).
2. **Record measurements** — garment templates (dress, shirt, and so on), units, and dated history per customer.
3. **Create orders** — one or more garment lines, each holding a **snapshot** of measurements at order time so later edits to the customer profile do not rewrite the job.
4. **Move work through production** — New → Cutting → Sewing → Fitting → Ready → Collected (or Cancelled). Overdue is derived when the due date has passed and the order is not finished.
5. **Collect money** — deposits and further payments (Cash, MoMo, Bank, Other). Outstanding balance = order total − sum of payments. Receipts for share or print.
6. **Stay honest offline** — the phone app saves to SQLite first (“Saved on this device”), queues changes, then syncs to the cloud when online without duplicating records.
7. **Oversee from the web** — owners and managers see a dashboard and a production board (status columns, assignees) fed only through the API.

### Who uses it

| Role | What they do |
|------|----------------|
| **Owner** | Full control: shop settings, staff, licence, payments, all orders |
| **Manager** | Operations: customers, orders, assign work, payments; not billing ownership unless granted |
| **Tailor** | Assigned work and related customers; update production; **no** Staff or License page; Payments hidden by default |

A solo tailor can run the whole shop as Owner with **zero** staff setup.

### Platforms

| Platform | What the user experiences |
|----------|---------------------------|
| **Mobile (MAUI)** | Bottom nav: Home · Customers · Orders · Payments · More. New order from Home, Orders and a customer page. Primary daily tool; full offline MVP. |
| **Web** | Left sidebar: Dashboard, Production, Orders, Customers; and for the right roles Payments, Staff, WhatsApp, Activity and License. Measurements live on the customer pages. Online only, via the API. |
| **Api + PostgreSQL** | Auth, tenancy, sync endpoints, CRUD; JWT + refresh; claims `sub`, `business_id`, `role`. |

### Priority journeys

End-to-end acceptance checks. Detail: [journeys.md](.cursor/skills/needlebase-uiux/journeys.md).

| # | Journey | Outcome |
|---|---------|---------|
| 1 | Register shop → customer → measurements → order → deposit → status | End-to-end happy path (mobile primary) |
| 2 | Create order offline → reopen → sync | Data persists; **one** order after sync (no duplicates) |
| 3 | Update status / assign staff → see on web | Production board and dashboard reflect mobile changes |
| 4 | Additional payment → balance → receipt | Outstanding updates; receipt works offline with a pending note |
| 5 | Invite staff as Tailor | Limited nav and permissions |

## Domain model

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
| **Business** | The shop | Name, phone, default currency GHS, units |
| **User** + **StaffMembership** | Identity in a shop | Role: Owner / Manager / Tailor |
| **Invitation** | Staff onboarding | Email/phone, role, expiry; accept joins membership |
| **Customer** | Client of the shop | Name, phone, notes, optional photo |
| **MeasurementProfile** | Body / garment measures | CustomerId, garment template, fields, unit, dated history |
| **Order** | One customer order | CustomerId, readable/ULID ref, status, due/fitting dates, assignee, totals |
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

**Overdue** = the due date has passed and the status is not Collected or Cancelled (a badge, not a stored status).

### Payments

- Methods: **Cash**, **MoMo**, **Bank**, **Other** (extensible).
- Balance: `order total − Σ(payments)`.
- Offline payments are local and pending sync. The UI must not imply that an external provider confirmed them.
- Sync conflicts on payments are **never silently overwritten**. The API returns a conflict payload; the UI resolves it.

### Offline sync

- Saves go to SQLite and enqueue unless pull apply sets `SuppressSyncQueue`.
- Push and pull since `LastSync`; deletes apply via `DeletedItem`.
- SyncChip states: Offline · Waiting to sync · Syncing · Synced · Failed.
- A transient network or refresh failure does **not** clear tokens or empty the queue.

### Hard product rules

- A local save is not a cloud sync — use copy like “Saved on this device”.
- Solo shops work with zero staff.
- Default money: **en-GH / GH₵ (GHS)**.
- Do not invent multi-currency or multi-shop switching until asked.

### Out of scope (until asked)

Multi-currency runtime, multi-shop switching UI, full web offline parity, payment-provider webhooks as source of truth, AI features, Redis, Mediator “for later”, Excel/PDF reporting suites.

## Implementation slices

Most slices are built. Confirm with the user before starting later work. Acceptance and leftover journeys: [phases.md](.cursor/skills/needlebase-phases/phases.md).

| Slice | Status | What it covers |
|-------|--------|----------------|
| **0** Scaffold | **Built** | Solution, Clean Architecture projects, health endpoints, README, package baselines, Web/Mobile shells |
| **1** Auth + Business | **Built** | Register shop + Owner; login; JWT + refresh; `business_id` / `role` claims; mobile secure token store + session; Web cookie/session + HttpClients; BusinessId global query filters |
| **2** Customers + measurements | **Built** | Domain + Api CRUD (paginated, Business-scoped); mobile Customers tab + measurement capture; local SQLite save. Web customers arrived in slice 6. |
| **3** Orders + production | **Built** | Orders/lines; status pipeline; mobile New Order wizard + Orders tab; Api list/detail |
| **4** Offline sync | **Built** (device journeys not yet verified) | Sync queue, push/pull, `DeletedItem`, SyncChip / Sync details, idempotent ULIDs, token honesty under offline |
| **5** Payments | **Built** (device journeys not yet verified) | Record deposits/payments, balances, receipts; conflict-safe sync + UI resolution |
| **6** Web oversight | **Built** (browser journey not yet verified) | Sidebar shell; Dashboard; Production board; Orders via Api HTTP |
| **7** Staff + roles | **Built** (device journey not yet verified) | Invitations, role policies, role-filtered mobile/web nav, password reset when email is configured |
| **8** Licensing and access | **Built** for Platform licence keys, daily recheck, a grace period and the read-only lock. WhatsApp tap-to-send and automatic sending are in code. PlanCatalog, server staff caps, Free, upgrade from Free, export/usage and paid emails (plans steps 1–6) are built. **Step 7 in progress:** PDF receipts, customer email and profit/monthly reports done; photos next via Cloudflare R2. | Licence key attach, plan/staff usage, export (incl. read-only), status banners. Free = phone only with meters; Go online keeps phone records. |

## Solution layout

```
NeedleBase.sln
├── src/
│   ├── NeedleBase.Domain/           # Entities, enums — no infra deps
│   ├── NeedleBase.Application/      # Use cases, contracts/DTOs, NUlid IDs
│   ├── NeedleBase.Infrastructure/   # EF PostgreSQL, Identity
│   ├── NeedleBase.Api/              # ASP.NET Core host
│   ├── NeedleBase.Web/              # Razor Pages — HTTP to Api only (no SQL)
│   ├── NeedleBase.Mobile/           # .NET MAUI (MVVM)
│   └── NeedleBase.Mobile.Data/      # SQLite LocalDbContext, sync queue and device info
└── tests/
    ├── NeedleBase.Domain.Tests/
    ├── NeedleBase.Application.Tests/
    ├── NeedleBase.Api.Tests/
    ├── NeedleBase.Web.Tests/
    └── NeedleBase.Mobile.Tests/
```

**Dependency rule:** Domain ← Application ← Infrastructure. The Api composes Application + Infrastructure. The Web uses Application contracts and HTTP to the Api (never a DbContext). Mobile uses Mobile.Data + Application (never Infrastructure or Api project references).

Skills and architecture notes: [.cursor/skills/](.cursor/skills/).

### Package baselines

| Area | Package | Version target |
|------|---------|----------------|
| IDs | NUlid (`Ulid.NewUlid().ToString()`) | 1.7.3 |
| API EF | Npgsql.EntityFrameworkCore.PostgreSQL | 9.0.4 |
| Mobile EF | Microsoft.EntityFrameworkCore.Sqlite | 9.0.9 |
| MVVM | CommunityToolkit.Mvvm / CommunityToolkit.Maui | 8.4.0 / 11.2.0 |

Do **not** add Mediator, Redis, or other infrastructure “for later” without a measured need.

## Local development

**Prerequisites:** .NET 9 SDK, Docker (for local Postgres), and for the phone app the `maui-android` workload:

```bash
dotnet workload install maui-android
```

### 1. Database

```bash
docker compose up -d
```

Postgres: `localhost:5435`, database `NeedleBase`, user `needlebase`, password `needlebase_dev` (Development defaults in `appsettings.Development.json`).

### 2. Restore, build and test

```bash
dotnet restore NeedleBase.sln
dotnet build NeedleBase.sln
dotnet test NeedleBase.sln
```

After code changes, use a clean Release build with 0 warnings: `dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror`.

### 3. Run from the CLI

```bash
dotnet run --project src/NeedleBase.Api --launch-profile http
dotnet run --project src/NeedleBase.Web --launch-profile http
```

| App | Development URL | Health |
|-----|-----------------|--------|
| Api | http://127.0.0.1:5036 | `/health/live`, `/health/ready` (DB) |
| Web | http://localhost:5272 | Home page probes Api `/health/live` |

Mobile ping: `GET /api/test/ping`.

### 4. Debug in Cursor or VS Code

| Launch config | What it starts |
|---------------|----------------|
| **NeedleBase (Api + Mobile USB)** | Compound: preflight (Postgres + `adb reverse` on the single connected USB phone + free `:5036`) → debug **Api** (`coreclr`) + **Mobile (Android USB)** (`maui` with stock `maui: Build`; no serial is pinned, so it deploys to the phone selected in the status bar). Preflight fails if an emulator is also ready (prevents reverse/deploy mismatch). Stop (`stopAll`) runs `api: stop` + `mobile: stop`. |
| **NeedleBase (Api + Mobile Emulator)** | Same compound for `Pixel_7_API35` / `emulator-5554` (auto-starts AVD when the pin is absent; fails fast if any other emulator is listed while the pin is missing, or if other ready emulators exist while the pin is not `device`; longer adb wait). |
| **NeedleBase (Api + Web)** | Api + Web compound (`stopAll`). |
| **Api (Development)** | Api alone with breakpoints (`console` + `api: stop`). |
| **Web (Development)** | Web alone. |
| **Mobile (Android USB)** / **Mobile (Android Emulator)** | MAUI only (`preLaunchTask` must stay exactly `maui: Build`). |

Phone USB debug:

1. Enable Developer options + USB debugging; connect the phone (`adb devices`).
2. Status bar → **NeedleBase.Mobile** / `net9.0-android` / your phone. The USB launch pins no serial: preflight requires **exactly one** ready USB phone and **no** ready emulator (otherwise reverse and status-bar deploy can diverge). Stop extras or use **Api + Mobile Emulator**. Optional gitignored `.vscode/needlebase.local.env` can set `NEEDLEBASE_DEVICE` / other `NEEDLEBASE_*` for scripts and `ide-f5-verify.sh` (integrated-terminal `export` does **not** reach VS Code tasks). Only the emulator launch still pins `emulator-5554`.
3. Run and Debug → **NeedleBase (Api + Mobile USB)** → Start Debugging (or **Api + Mobile Emulator** without a phone). Prefer the compounds: they run preflight (`adb reverse`). Standalone **Mobile (Android USB/Emulator)** keeps `preLaunchTask` = `maui: Build` (a MAUI requirement) and does **not** set reverse by itself.
4. Optional check: `bash .vscode/ide-f5-verify.sh` (validates the launch.json contract and runs the real `api-mobile-preflight.sh` plus start/stay/stop on the phone).

Compound Mobile DEBUG uses `http://127.0.0.1:5036/` via `adb reverse` from preflight.

Do **not** change Mobile’s `preLaunchTask` away from `maui: Build` — the MAUI extension will not deploy otherwise.

### Configuration

| Setting | Development default |
|---------|---------------------|
| Postgres | `localhost:5435`, db `NeedleBase`, user `needlebase` |
| Api | `http://127.0.0.1:5036` |
| Web → Api | `Api:BaseUrl` = `http://127.0.0.1:5036` |
| JWT key | Development key in Api `appsettings.Development.json` only |

Override with user secrets or environment variables when needed (`ConnectionStrings__DefaultConnection`, `JwtSettings__SecurityKey`, `Api__BaseUrl`). Do not use Development keys in Production.

**Migrations:** Development auto-applies `dotnet ef` migrations on Api startup. Production never does: apply the idempotent script from `scripts/db-migrate.sh` as a deploy step ([docs/deployment.md](docs/deployment.md)).

**Development seed:** On Api startup in Development (when `Seed:DevelopmentData` is true), sample Ama Stitches data is inserted if missing:

| Account | Email | Password | Role |
|---------|-------|----------|------|
| Ama Owusu | `ama@amastitches.dev` | `DevPass1!` | Owner |
| Kofi Asante | `kofi@amastitches.dev` | `DevPass1!` | Tailor |

It also seeds customers (Efua Mensah, Kwame Boateng) with sample measurements. Disable with `"Seed": { "DevelopmentData": false }`.

### Auth endpoints

| Method | Path | Notes |
|--------|------|--------|
| POST | `/api/auth/register-business` | Creates Business + Owner; returns tokens + user |
| POST | `/api/auth/login` | Returns tokens + user (`business_id`, `role`) |
| POST | `/api/auth/refresh-token` | Rotates refresh token |
| POST | `/api/auth/logout` | Revokes the refresh token |
| GET | `/api/auth/options` | Whether email/password reset is available |
| POST | `/api/auth/forgot-password` | Request a reset (same reply if the email is unknown) |
| POST | `/api/auth/reset-password` | Set a new password with the reset code |
| POST | `/api/auth/invite-preview` | Preview a staff invite |
| POST | `/api/auth/accept-invite` | Accept a staff invite |
| POST | `/api/auth/change-password` | Change the signed-in user's password (JWT required) |
| GET | `/api/me` | Requires JWT + `business_id` + staff role |
| POST | `/api/me/delete-account` | Delete this login, or close the shop if the user is the Owner (JWT required) |

The phone app may send `X-Auth-Token: Bearer …` instead of `Authorization` (mapped on the Api).

`ApplicationDbContext` uses **global query filters** on `IBusinessScoped` entities when `CurrentBusinessId` is set from the JWT by `BusinessTenancyMiddleware`. Auth and bootstrap paths clear the filter by leaving it null.

## Account deletion

In the phone app: **More → Your account → Delete account**. On the website: `/account/delete` (a signed-in form, or instructions to email or WhatsApp when signed out).

- A **staff** delete removes only that login.
- An **Owner** delete closes the shop, signs everyone out, and removes the shop's customers, measurements, orders, payments and WhatsApp data. An anonymised shop record and the activity log remain (the log is kept for up to 2 years).

## Legal and licensing

Published Privacy Policy and Terms live in [docs/legal/](docs/legal/). The same wording is on the website and in the app. Internal notes for keeping those copies in sync, and for gaps the documents cannot fix, are in [docs/legal/README.md](docs/legal/README.md).

Shops are licensed through Platform. See [docs/platform-licensing.md](docs/platform-licensing.md). WhatsApp setup: [docs/whatsapp-setup.md](docs/whatsapp-setup.md).

## Pricing (decided; Free + upgrade built)

These prices and limits are decided. **PlanCatalog** maps plan codes to entitlements; **staff caps** are enforced on the server (invite/accept/reactivate). **Free**, **upgrade from Free**, **export/usage display** and **paid emails** (steps 1–6) are built. **Step 7** is in progress: PDF receipts, customer email and profit/monthly reports are built; **photos next via Cloudflare R2**. The public landing page and the published Terms show these prices (updated 18 November 2026). Prices are in Ghana cedis and **do not include VAT**. The full plan (PlanCatalog rules, implementation order, risks, and how this works with Platform) is in [.cursor/skills/needlebase-plans/SKILL.md](.cursor/skills/needlebase-plans/SKILL.md).

| Plan | Price | Who | What it includes |
|------|-------|-----|------------------|
| **Free** | GH₵0 | Owner only, on the phone | 10 customers and 20 orders ever created (deleting either one does not free a slot). No account, no sync, no website. WhatsApp by tap-to-send. "Save a copy" export only. |
| **Starter** | GH₵50 / month, GH₵140 / quarter, GH₵500 / year | Owner + 2 staff | Sync, a cloud backup, the website, unlimited customers and orders, a monthly summary email to the Owner, export, and the shop's phone number on receipts. This is a launch price and may rise later. Existing shops move to the new price after 30 days' notice. |
| **Workshop** | GH₵150 / month, GH₵430 / quarter, GH₵1,500 / year | Owner + 8 staff | Everything in Starter (including the monthly summary email), plus assignment and due-date emails, a weekly summary email to the Owner that includes the balances owed, photos, PDF receipts, and profit and monthly reports. |
| **Production** | GH₵300 / month, GH₵850 / quarter, GH₵3,000 / year | Owner + 20 staff | Everything in Workshop, plus more photo storage and a setup call. |

A year costs 10 months. There is no free trial and no founding-shop discount. A referral offer is planned. When a paid shop stops paying, the existing read-only lock applies and the shop can still export.

**Built (25 Nov 2026):** PlanCatalog; server staff caps; Free phone-only mode (no account, local counters, Save a copy, welcome-path diction); upgrading from Free (licence key, keep local records, first sync, resume-safe); export and usage display (License plan/staff meters, paid/lapsed export, Free `7/10` meters, shop phone on receipts).

**Not built yet:** upgrade prompts beyond License/usage. Workshop step 7: PDF receipts and profit/monthly reports are built (reports show money in and owed; NeedleBase records no costs yet); photos (Cloudflare R2) not started — still listed on the landing page and in the Terms. Automatic WhatsApp template messages exist in code but are **not** a sold plan feature until Meta verifies the business.

## Further documentation

| Doc | What it covers |
|-----|----------------|
| [docs/deployment.md](docs/deployment.md) | One-site deploy to MonsterASP.NET, migrations, publish |
| [docs/production-config.md](docs/production-config.md) | Production settings and start-up checks |
| [docs/ci-cd.md](docs/ci-cd.md) | GitHub Actions, release tags, deploy |
| [docs/android-release.md](docs/android-release.md) | Sideload APK and Play AAB |
| [docs/platform-licensing.md](docs/platform-licensing.md) | How shops are licensed through Platform |
| [docs/whatsapp-setup.md](docs/whatsapp-setup.md) | Tap-to-send and automatic WhatsApp setup |
| [docs/legal/](docs/legal/) | Privacy Policy, Terms, and internal legal notes |
| [docs/landing-photos/README.md](docs/landing-photos/README.md) | Landing-page photo sources |
| [.cursor/skills/needlebase-plans/SKILL.md](.cursor/skills/needlebase-plans/SKILL.md) | Decided plans, limits and the implementation order (PlanCatalog + staff caps built; Free not yet) |
| [.cursor/skills/](.cursor/skills/) | Agent skills and standing rules |
