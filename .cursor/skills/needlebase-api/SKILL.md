---
name: needlebase-api
description: >-
  Implements NeedleBase ASP.NET Core API and Infrastructure using Clean
  Architecture: JWT auth, Business tenancy, EF SQL Server, sync push/pull,
  DeletedItem tracking, health endpoints, and high-performance query patterns.
  Use when working on Api, Infrastructure, controllers, migrations, identity,
  or server-side sync.
---

# NeedleBase API & Infrastructure

Composition: **Api** host + **Infrastructure** implementations of **Application** interfaces. Domain stays persistence-ignorant.

Learned from AssetTag API: JWT + security stamp, `X-Auth-Token` mapping, DeletedItem on save, sync lock, health/ping, ULID, en-GH money defaults — plus **BusinessId** tenancy NeedleBase requires.

## Hard rules

1. **ULID string IDs** — never int/Guid PKs for domain entities.  
2. **BusinessId filter** — every tenant query/command; enforce from claims.  
3. **DeletedItem** — `SaveChanges` tracks deletes for mobile pull; include BusinessId. Do not bypass without sync awareness.  
4. **Sync push** — authorize user; scope to business; optional distributed lock per user/business (409 if busy). Idempotent on client entity ULIDs.  
5. **Payment conflicts** — do not silently overwrite; return conflict payload for mobile UI.  
6. **Auth** — JWT Bearer + refresh tokens; map `X-Auth-Token` → `Authorization` for mobile. Do not log tokens in Production.  
7. **Health** — `/health/live`, `/health/ready` (DB), mobile connectivity ping (e.g. `/api/test/ping`).  
8. **Migrations** — prefer explicit `dotnet ef database update` / CI job (AssetTag style); document if auto-migrate is enabled.  
9. **Email links** — use `FrontendBaseUrl` config; never hardcode Web hostnames.  
10. **Thin endpoints** — controllers call Application use cases, not raw SQL sprawl.  
11. **Speed & EF hygiene** — `AsNoTracking()` + DTO projection for reads; index `BusinessId` (and common filter/sort columns); no N+1; paginate lists with a hard max page size; pass `CancellationToken`; batch sync apply/`SaveChanges`; prefer `ExecuteUpdate`/`ExecuteDelete` only when DeletedItem/sync side-effects are handled. Full rules: [code-quality-performance](../needlebase-architecture/code-quality-performance.md).  
12. **Inspect before coding** — Follow [engineering-review](../needlebase-architecture/engineering-review.md): inspect first, fix root cause (no parallel workarounds), remove obsolete code, verify with evidence.

## Layout

```
NeedleBase.Api/
  Controllers/ or Endpoints/   Auth, Customers, Orders, Measurements,
                               Payments, Sync, Staff, Invitations,
                               Dashboard, Subscription, Diagnostics
  Middleware/
  Program.cs

NeedleBase.Infrastructure/
  Persistence/ApplicationDbContext.cs
  Persistence/Configurations/
  Identity/
  Sync/
  Email/
  DependencyInjection.cs
```

## Auth & roles

- Roles: Owner, Manager, Tailor (per business membership)  
- Policies: e.g. `RequireOwner`, `RequireManagerOrOwner`, `RequireStaff`  
- Tailor: limit to assigned orders where applicable  

## Sync API (contract alignment)

- `POST api/sync/push` — body of queued operations; returns success/fail/conflict per op id  
- Pull — changes since `LastSync` + DeletedItems for business  
- DTOs live in Application contracts; Mobile.SyncService must match  

## Culture & money

Default `en-GH` / GH₵ formatting for MVP; keep consistent with Web/Mobile. Design fields for future currency codes without implementing multi-currency until needed.

## Local

```bash
dotnet run --project src/NeedleBase.Api --launch-profile https
```

SQL Server connection via user secrets / `appsettings.Development.json` — never commit secrets.

## Related

- [needlebase-architecture](../needlebase-architecture/SKILL.md)  
- [needlebase-domain](../needlebase-domain/SKILL.md)  
- [needlebase-mobile-sync](../needlebase-mobile-sync/SKILL.md)  
