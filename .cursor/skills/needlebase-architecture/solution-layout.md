# Solution layout

`[now]` = exists today · `[target]` = add when the slice needs it (don't create empty folders).

```
NeedleBase.sln
docker-compose.yml                 # Postgres 16 on localhost:5435 (dev/tests)
src/
  NeedleBase.Domain/
    Common/FieldLimits.cs  Entities/  Enums/  Exceptions/          [now]
  NeedleBase.Application/
    Abstractions/   (IApplicationDbContext, IAuthService, IClock, ICurrentUser, ICustomerService, IMeasurementService, ITokenService) [now]
    Common/         Contracts/{Auth,Customers,Errors,Measurements}/ [now]  (Errors = ApiProblem reader + AuthErrorMessages, shared by Web/Mobile)
    Customers/      (CustomerService, MeasurementService)          [now]
    Orders/  Payments/  Sync/  Staff/                              [target, per slice]
  NeedleBase.Infrastructure/
    Auth/  Identity/  Persistence/  Health/  Time/  DependencyInjection.cs [now]
    Sync/  Email/                                                  [target]
  NeedleBase.Api/
    Controllers/ (Auth, Customers, Measurements, Me, Diagnostics)  Http/ApiProblems.cs  Middleware/  Program.cs [now]
  NeedleBase.Web/          Razor Pages + Bootstrap (decided)
    Pages/{Account,Shared}  Services/ (ApiAuthService, TokenRefreshHandler)  wwwroot/  Program.cs [now]
  NeedleBase.Mobile/
    Views/  ViewModels/  Services/  Configuration/  Messaging/  Resources/  AppShell.xaml  MauiProgram.cs [now]
  NeedleBase.Mobile.Data/
    Data/{LocalDbContext,LocalDbOptions}.cs  Services/ (Customer/Measurement local services, LocalDatabaseService) [now]
    Data/LocalSchemaMigrator.cs  (versioned steps, PRAGMA user_version) [now]
tests/  Domain.Tests  Application.Tests  Api.Tests  Mobile.Tests   [now] — see testing.md
```

## Scaffold order

Follow the slice plan in [phases.md](../needlebase-phases/phases.md): one slice at a time, confirm before the next.

## Local run

```bash
docker compose up -d                       # Postgres for Api + Api.Tests
dotnet build NeedleBase.sln
dotnet run --project src/NeedleBase.Api --launch-profile https
dotnet run --project src/NeedleBase.Web --launch-profile https
dotnet build src/NeedleBase.Mobile -f net9.0-android
dotnet test
```

Api and Web are separate processes. Hosts and secrets come from config (user secrets locally, environment variables in production). Deployment/ops guidance is deferred; see Deferred in [code-gaps.md](../needlebase-phases/code-gaps.md).
