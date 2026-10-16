# NeedleBase

Offline-first tailoring business management (HelmutCode). Clean Architecture on **.NET 9**: ASP.NET Core API, Razor Pages web oversight, .NET MAUI mobile + SQLite.

## Solution layout

```
NeedleBase.sln
├── src/
│   ├── NeedleBase.Domain/           # Entities, enums — no infra deps
│   ├── NeedleBase.Application/      # Use cases, contracts/DTOs, NUlid (ULID string) IDs
│   ├── NeedleBase.Infrastructure/   # EF SQL Server, Identity (from Slice 1)
│   ├── NeedleBase.Api/              # ASP.NET Core host
│   ├── NeedleBase.Web/              # Razor Pages — HTTP to Api only (no SQL)
│   ├── NeedleBase.Mobile/           # .NET MAUI (MVVM)
│   └── NeedleBase.Mobile.Data/      # SQLite LocalDbContext (from Slice 4)
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
| API EF | Microsoft.EntityFrameworkCore.SqlServer | 9.0.9 |
| Mobile EF | Microsoft.EntityFrameworkCore.Sqlite | 9.0.9 |
| MVVM | CommunityToolkit.Mvvm / CommunityToolkit.Maui | 8.4.0 / 11.2.0 |

Do **not** add Mediator, Redis, or other infra “for later” without a measured need.

## Prerequisites

- .NET 9 SDK
- For mobile: `maui-android` workload (`dotnet workload install maui-android`)
- SQL Server (or LocalDB / Docker) — required from Slice 1 onward

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
| Api | https://localhost:7079 | `/health/live`, `/health/ready` (ready stub until SQL in Slice 1) |
| Web | https://localhost:7049 | Home page probes Api `/health/live` |

Mobile ping: `GET /api/test/ping`

### Secrets (never commit)

| Setting | Where | Notes |
|---------|--------|--------|
| `ConnectionStrings:DefaultConnection` | Api user secrets / env | Empty in committed appsettings; set locally for Slice 1+ |
| `Api:BaseUrl` | Web `appsettings.Development.json` or user secrets | Web **fails startup** if missing |
| `FrontendBaseUrl` | Api config | Used for email links later — no hardcoded hosts |
| JWT keys | Api user secrets (Slice 1) | — |

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;Database=NeedleBase;..." --project src/NeedleBase.Api
dotnet user-secrets set "Api:BaseUrl" "https://localhost:7079" --project src/NeedleBase.Web
```

Environment variables also work (`ConnectionStrings__DefaultConnection`, `Api__BaseUrl`).

## Culture / money

Default `en-GH` with ₵ (GHS). Multi-currency runtime is out of scope until requested.

## Implementation slices

See `.cursor/skills/needlebase-phases/phases.md`. Deliver one slice at a time.
