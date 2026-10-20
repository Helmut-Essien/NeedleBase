# Clean Architecture rules

## Where code lives (decided)

**Target:** use-case services live in **Application**. **Infrastructure** only implements persistence and external interfaces (EF `ApplicationDbContext`, Identity, token service, clock, email, file storage). Controllers and PageModels stay thin.

| Kind of code | Project | Example |
|--------------|---------|---------|
| Entities, invariants, constants | Domain | `Customer`, `BusinessRole`, `GarmentTemplate`, `FieldLimits` |
| Use-case services + their interfaces | Application | `ICustomerService` + `CustomerService` |
| Persistence port | Application | `IApplicationDbContext` (DbSets + `SaveChangesAsync`); Application may reference **EF Core abstractions only** for this port, never Npgsql or Infrastructure |
| DTOs / contracts | Application `Contracts/<Area>/` | `Contracts/Customers/*Dtos.cs` |
| EF config, migrations, Identity, JWT, clock | Infrastructure | `ApplicationDbContext`, `Identity/AuthService.cs`, `Time/` |
| HTTP surface | Api | `Controllers/*`, `Http/ApiProblems.cs`, `Middleware/` |

[code] `CustomerService` and `MeasurementService` live in `src/NeedleBase.Application/Customers/` behind `IApplicationDbContext` (Infrastructure's `ApplicationDbContext` implements it; registered in `Infrastructure/DependencyInjection.cs`). Identity `AuthService` stays in Infrastructure (it wraps ASP.NET Identity).

## Layer responsibilities

- **Domain:** entities, value constants, invariants (e.g. payment amount > 0). No EF attributes; mapping is fluent config in Infrastructure.
- **Application:** use cases, interfaces (`ICurrentUser`, `IClock`, `ITokenService`, `IApplicationDbContext`), DTO records, manual mapping, authorization *requirements* by role + `BusinessId`.
- **Infrastructure:** implements Application interfaces; PostgreSQL via Npgsql; global tenancy filters (fail-closed, see below); planned: sync push/pull server side, `DeletedItem`.
- **Api:** composition root, thin controllers, auth (JWT + `X-Auth-Token` mapping), tenancy middleware, ProblemDetails, health.
- **Web:** Razor Pages UI; HTTP to Api only; never EF.
- **Mobile + Mobile.Data:** MVVM; ViewModels call local services (`ICustomerLocalService`…) that read and write SQLite.

## Tenancy: fail closed

Every tenant entity carries `BusinessId` and has a global query filter `x.BusinessId == CurrentBusinessId`. With **no business id, a query returns no rows**. Code that must cross tenants (login, refresh, membership checks, seeding) calls `.IgnoreQueryFilters()` **with an explicit `UserId`/`BusinessId` predicate**, and that call is reviewed. Every tenant entity needs a cross-tenant isolation test ([testing.md](testing.md)).

[code] Filters are `CurrentBusinessId != null && x.BusinessId == CurrentBusinessId` (`ApplicationDbContext.cs`); login/refresh membership lookups in `Identity/AuthService.cs` use `.IgnoreQueryFilters()` scoped by user id. Tests: `TenancyIsolationTests`.

## Use-case flow

- **Online (Api/Web):** Controller → Application service → domain validate → `IApplicationDbContext.SaveChangesAsync` → DTO / `ApiProblems.*`.
- **Offline (Mobile):** ViewModel → local service → SQLite save → (Slice 4) sync queue → toast "Saved on this device".

## Anti-patterns

Domain referencing EF/MAUI · PageModel with DbContext · ViewModel touching PostgreSQL · DTOs duplicated in Api/Mobile · business rules only in controllers or ViewModels · skipping `BusinessId` "for convenience" · `IgnoreQueryFilters()` without an explicit predicate.
