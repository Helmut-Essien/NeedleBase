---
name: needlebase-architecture
description: >-
  Defines NeedleBase solution Clean Architecture, project layout, dependency
  rules, code quality and performance standards, senior engineering review
  before code changes, and patterns learned from AssetTag. Use when scaffolding
  the solution, choosing layers, adding projects, wiring DI, deciding where code
  belongs (Domain, Application, Infrastructure, Api, Web, Mobile, Mobile.Data),
  fixing bugs, refactoring, or enforcing fast, best-practice generated code.
---

# NeedleBase Architecture

Offline-first tailoring SaaS (HelmutCode). Stack target: **.NET 9**, ASP.NET Core API, responsive web, **.NET MAUI** mobile.

**Learned from AssetTag** (`/home/helmut/Documents/My Projects/AssetTag/`): Shared contracts, Portal-over-HTTP, MAUI MVVM + SQLite offline queue, JWT refresh honesty, ULID string IDs (NUlid), sync push/pull + DeletedItem.

**Elevated for NeedleBase:** Full **Clean Architecture** layers + **Business (workspace) multi-tenancy**. Mobile presentation is **MVVM** (CommunityToolkit.Mvvm).

UI/UX identity lives in [needlebase-uiux](../needlebase-uiux/SKILL.md) — apply it when building screens.

## Solution layout (target)

```
NeedleBase.sln
├── src/
│   ├── NeedleBase.Domain/              # Entities, enums, domain rules — no infra deps
│   ├── NeedleBase.Application/         # Use cases, interfaces, DTOs/contracts
│   ├── NeedleBase.Infrastructure/      # EF PostgreSQL, Identity, email, file, sync server
│   ├── NeedleBase.Api/                 # ASP.NET Core host + controllers/endpoints
│   ├── NeedleBase.Web/                 # Responsive web UI (no direct SQL)
│   ├── NeedleBase.Mobile/              # MAUI Views + ViewModels (MVVM)
│   └── NeedleBase.Mobile.Data/         # SQLite LocalDbContext, mobile-only sync tables
└── tests/
    ├── NeedleBase.Domain.Tests/
    ├── NeedleBase.Application.Tests/
    ├── NeedleBase.Api.Tests/
    └── NeedleBase.Mobile.Tests/
```

### Dependency rule

```
Domain  ←  Application  ←  Infrastructure
                ↑                ↑
               Api              Api (composes Infrastructure)
                ↑
               Web  ──HTTP──►  Api

Mobile  →  Mobile.Data  →  Application contracts / shared DTOs
                (never reference Infrastructure or Api projects)
```

| Layer | May reference | Must not |
|-------|---------------|----------|
| Domain | Nothing (or minimal shared kernels) | EF, HTTP, MAUI, UI |
| Application | Domain | EF Core, ASP.NET, MAUI |
| Infrastructure | Application, Domain | Api/Web/Mobile hosts |
| Api | Application + Infrastructure (composition) | Mobile |
| Web | Application DTOs only via HTTP clients | DbContext / SQL |
| Mobile | Application contracts, Mobile.Data | Infrastructure, Api project refs |
| Mobile.Data | Domain entities or mobile-mapped models + Application sync DTOs | Api, Web |

Details: [clean-architecture.md](clean-architecture.md), [solution-layout.md](solution-layout.md).

## What to reuse from AssetTag

| Pattern | AssetTag source | NeedleBase adaptation |
|---------|-----------------|------------------------|
| Shared DTOs / ULID string IDs | `Shared/` | Put contracts in **Application** (and/or thin `Contracts` folder); IDs = ULID strings via NUlid |
| Portal never touches DB | `Portal/` | **Web** calls Api over HTTP only |
| MAUI MVVM + BaseViewModel | `MobileApp/ViewModels` | Same Toolkit patterns; Clean Architecture use-cases behind services |
| SQLite + sync queue on SaveChanges | `MobileData/LocalDbContext` | Same; add `BusinessId` on all tenant data |
| JWT + TokenRefreshResult honesty | `AuthService`, sync skill | Same hard rules |
| DeletedItem for pull deletes | API `ApplicationDbContext` | Same + scoped by BusinessId |
| Named HttpClients + refresh handler | Portal / Mobile | Same |
| Culture en-GH / money | Program.cs | Default GHS; design for multi-currency later |
| UI icons via MauiIcons.Material | `UseMaterialMauiIcons()` + `{mi:Material Icon=…}` | Tabs and in-page chrome — **no PNG UI icons**; see [tab-icons.md](../needlebase-mobile/tab-icons.md) |

## What NOT to copy from AssetTag

- Asset / barcode / campus / depreciation domain
- Single-tenant org (NeedleBase = **Business workspace** isolation)
- Admin/User as only roles → use Owner / Manager / Tailor
- “Server wins” silent conflicts for **payments** → explicit user resolution
- Claiming Scoped DI in docs while registering Singleton — document **actual** lifetimes (see mobile skill)
- Product branding, ApplicationIds, hosted AssetTag URLs
- Legacy Shell `tab_*.png` assets (AssetTag still has some) — NeedleBase uses MauiIcons for **all** UI icons including TabBar
- UraniumUI **solely for icons** — MauiIcons.Material is enough for glyphs; add Uranium only if you need its controls

## Cross-cutting hard rules

1. **Business isolation** — Every tenant entity carries `BusinessId`. Queries, sync, and auth claims must filter by it. Never mix workspaces.
2. **ULID string IDs** — `IdGenerator.NewId()` / `Ulid.NewUlid().ToString()` (NUlid) for all domain entity PKs and FKs (including `BusinessId`). Never `int`/`Guid` PKs.
3. **Offline honesty** — Local save ≠ cloud sync. Payments pending sync must not look provider-confirmed. See [needlebase-mobile-sync](../needlebase-mobile-sync/SKILL.md) and UI offline-sync.
4. **Web has no SQL** — Web → Api HTTP only (AssetTag Portal pattern).
5. **Mobile has no Api project reference** — talk via HTTP + local DB.
6. **UI** — Follow [needlebase-uiux](../needlebase-uiux/SKILL.md) for nav, tokens, copy.
7. **Code quality & speed** — All generated code must follow best practices and be highly optimised for speed on real hot paths (queries, sync, UI binds, HTTP). See [code-quality-performance.md](code-quality-performance.md).
8. **Inspect before coding** — Follow [engineering-review.md](engineering-review.md): inspect first, fix root cause (no v1+v2 workarounds), remove obsolete code, check regressions, keep the fix minimal, verify with evidence, then report root cause / changes / removals / risks.

## Skill map

| Area | Skill |
|------|-------|
| Layers & scaffolding | This skill |
| Implementation slices / phases | [needlebase-phases](../needlebase-phases/SKILL.md) |
| Pre-change engineering review | [engineering-review.md](engineering-review.md) |
| Code quality & speed | [code-quality-performance.md](code-quality-performance.md) |
| Domain entities, roles, tenancy | [needlebase-domain](../needlebase-domain/SKILL.md) |
| MAUI MVVM, Shell, DI | [needlebase-mobile](../needlebase-mobile/SKILL.md) |
| Offline SQLite sync | [needlebase-mobile-sync](../needlebase-mobile-sync/SKILL.md) |
| ASP.NET Api + Infrastructure | [needlebase-api](../needlebase-api/SKILL.md) |
| Responsive web | [needlebase-web](../needlebase-web/SKILL.md) |
| Screens & design system | [needlebase-uiux](../needlebase-uiux/SKILL.md) |

## Default packages (align with AssetTag lessons)

| Area | Packages |
|------|----------|
| MVVM | CommunityToolkit.Mvvm, CommunityToolkit.Maui |
| Mobile data | EF Core + SQLite |
| API data | EF Core + PostgreSQL, ASP.NET Identity |
| IDs | NUlid |
| Auth | JWT Bearer + refresh tokens |
| Tests | xUnit + NSubstitute (or equivalent) |

Prefer Clean Architecture use-case services (or CQRS handlers) in Application over fat controllers/PageModels — improve on AssetTag’s thinner service layer without inventing unused abstractions.

## Code generation mandate

When writing or refactoring NeedleBase code under this skill map:

- **Before any code change**, follow [engineering-review.md](engineering-review.md) in full. Do not start coding until the existing implementation is inspected and the likely root cause is identified.
- Apply [code-quality-performance.md](code-quality-performance.md) by default (do not wait for the user to ask for “optimization”).
- Prefer async I/O, `AsNoTracking` reads, DTO projection, pagination, compiled MAUI bindings, batched sync, and cancellation tokens.
- Do **not** add speculative caches, Mediator, or infra solely for speed — optimise the measured path (DB round-trips, allocations, UI thread, sync lock contention).
- Never trade BusinessId isolation, payment conflict honesty, or offline correctness for throughput.
- Before finishing, report root cause, what changed and why, obsolete code removed, and remaining risks / unverified assumptions. Never claim a fix works without evidence.
