---
name: needlebase-architecture
description: >-
  NeedleBase Clean Architecture: project layout, dependency rules, where code
  lives (Application use cases vs Infrastructure), fail-closed Business
  tenancy, testing standards, code quality/performance, and the mandatory
  engineering review before any code change. Use when adding projects or
  folders, deciding where code belongs, wiring DI, writing tests, fixing bugs,
  refactoring, or reviewing generated code quality.
---

# NeedleBase Architecture

Offline-first tailoring SaaS (HelmutCode). **.NET 9**: ASP.NET Core Api (PostgreSQL), Razor Pages Web, .NET MAUI Android app with SQLite. Clean Architecture + **Business (workspace) multi-tenancy**; mobile presentation is MVVM (CommunityToolkit.Mvvm).

Layout (now vs target): [solution-layout.md](solution-layout.md). Layer rules: [clean-architecture.md](clean-architecture.md). Tests: [testing.md](testing.md). Gap backlog (done + deferred ops): [code-gaps.md](../needlebase-phases/code-gaps.md).

## Dependency rule

```
Domain ← Application ← Infrastructure ← Api (composition root)
Web ──HTTP──► Api                Mobile → Mobile.Data → Application contracts
```

| Layer | May reference | Must not |
|-------|---------------|----------|
| Domain | nothing | EF, HTTP, MAUI |
| Application | Domain; EF Core **abstractions** for `IApplicationDbContext` only | Npgsql, ASP.NET, MAUI, Infrastructure |
| Infrastructure | Application, Domain | Api/Web/Mobile |
| Api | Application, Infrastructure | Mobile, Web |
| Web | Application DTOs (via HTTP) | DbContext / SQL |
| Mobile, Mobile.Data | Application contracts, Domain constants | Infrastructure, Api |

## Cross-cutting hard rules

1. **Tenancy fails closed.** Every tenant entity has `BusinessId` and a global filter `x.BusinessId == CurrentBusinessId`. A null business id returns **no rows** (or the request is rejected). Cross-tenant reads only via reviewed `.IgnoreQueryFilters()` with an explicit predicate. Each tenant entity needs a cross-tenant isolation test.
2. **Use cases in Application**, Infrastructure implements ports ([clean-architecture.md](clean-architecture.md#where-code-lives-decided)).
   [code] `Application/Customers/{CustomerService,MeasurementService}.cs` depend on `Application/Abstractions/IApplicationDbContext` (Application references `Microsoft.EntityFrameworkCore` for that port only, no provider); Infrastructure's `ApplicationDbContext` implements it.
3. **ULID string ids** via `IdGenerator.NewId()` (NUlid) for all entity PKs/FKs. Never `int`/`Guid`.
4. **Offline honesty.** Local save ≠ synced. Pending payments never look provider-confirmed ([needlebase-mobile-sync](../needlebase-mobile-sync/SKILL.md)).
5. **Web has no SQL; Mobile has no Api/Infrastructure project reference.**
6. **Errors** are ProblemDetails with a stable `code` ([needlebase-api](../needlebase-api/SKILL.md#user-facing-errors-problemdetails)). Services (server and device) return typed results (`RecordResult<T>`/`RecordError`, `OrderResult`), never English strings; callers branch on the code. The shared code list lives only in needlebase-api rule 4.
7. **UI** follows [needlebase-uiux](../needlebase-uiux/SKILL.md).
8. **Quality & speed by default:** [code-quality-performance.md](code-quality-performance.md).
9. **Inspect before coding:** [engineering-review.md](engineering-review.md). Inspect first, fix the root cause, remove obsolete code, verify with evidence, report root cause / changes / removals / risks. Never claim a fix works without evidence (tests run, build output).

## Packages (actual)

CommunityToolkit.Mvvm + CommunityToolkit.Maui · AathifMahir.Maui.MauiIcons.Material · EF Core (Npgsql on Api, Sqlite on Mobile.Data) · ASP.NET Identity + JWT Bearer · NUlid · xunit + Mvc.Testing (no mocking library; see [testing.md](testing.md)).

Don't add Mediator/CQRS, caches, Redis or message buses without a measured need.

## Skill map

| Area | Skill |
|------|-------|
| Slices, status, code-gap backlog | [needlebase-phases](../needlebase-phases/SKILL.md) |
| Entities, roles, DTOs, add-entity checklist | [needlebase-domain](../needlebase-domain/SKILL.md) |
| Api, auth, errors | [needlebase-api](../needlebase-api/SKILL.md) |
| MAUI MVVM, Shell, DI | [needlebase-mobile](../needlebase-mobile/SKILL.md) |
| Offline sync (planned Slice 4) | [needlebase-mobile-sync](../needlebase-mobile-sync/SKILL.md) |
| Web | [needlebase-web](../needlebase-web/SKILL.md) |
| Screens, tokens, copy | [needlebase-uiux](../needlebase-uiux/SKILL.md) |
