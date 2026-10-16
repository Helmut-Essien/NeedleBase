# Solution Layout & Scaffolding Checklist

## Suggested folders (per project)

```
NeedleBase.Domain/
  Entities/
  Enums/
  Exceptions/

NeedleBase.Application/
  Abstractions/          # interfaces
  Contracts/DTOs/        # records shared with clients
  Customers/
  Orders/
  Measurements/
  Payments/
  Sync/
  Staff/
  Common/

NeedleBase.Infrastructure/
  Persistence/           # DbContext, configs, migrations
  Identity/
  Email/
  Sync/
  DependencyInjection.cs # AddInfrastructure()

NeedleBase.Api/
  Controllers/ or Endpoints/
  Middleware/
  Program.cs

NeedleBase.Web/
  Pages/ or Components/  # Razor / Blazor — choose one and stick to it
  Services/              # ApiAuthService, TokenRefreshHandler
  wwwroot/
  Program.cs

NeedleBase.Mobile/
  Views/
  ViewModels/
  Services/              # auth, navigation, sync facade, local app services
  Controls/
  Converters/
  Configuration/
  MauiProgram.cs
  AppShell.xaml

NeedleBase.Mobile.Data/
  Data/LocalDbContext.cs
  Migrations/
```

## AssetTag → NeedleBase name map

| AssetTag | NeedleBase |
|----------|------------|
| `Shared/` | `Application/Contracts` (+ Domain entities) |
| `AssetTag/` | `Api` + `Infrastructure` |
| `Portal/` | `Web` |
| `MobileApp/` | `Mobile` |
| `MobileData/` | `Mobile.Data` |

## Scaffold order

Prefer the slice plan in [needlebase-phases](../needlebase-phases/SKILL.md) (one slice at a time, confirm before next). Summary:

1. Create solution + Domain + Application (Slice 0)  
2. Infrastructure + Api (auth, health, Business entity) (Slices 0–1)  
3. Web shell calling Api (Slice 1 / 6)  
4. Mobile + Mobile.Data with offline Customer/Order MVP (Slices 2–3)  
5. Sync push/pull for MVP entities (Slice 4)  
6. Payments with conflict-safe rules (Slice 5)  
7. Web oversight board (Slice 6); Staff/roles (Slice 7); subscription surfaces (Slice 8)  

## Local run (mirror AssetTag)

```bash
dotnet restore NeedleBase.sln && dotnet build NeedleBase.sln
dotnet run --project src/NeedleBase.Api --launch-profile https
dotnet run --project src/NeedleBase.Web --launch-profile https
```

Api and Web are **separate processes**. Never hardcode production hostnames; use config / user secrets / CI secrets (`API_BASE_URL`, `FRONTEND_BASE_URL`).
