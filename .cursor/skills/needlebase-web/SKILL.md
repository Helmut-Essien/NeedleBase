---
name: needlebase-web
description: >-
  Implements NeedleBase responsive web oversight app (Razor Pages or Blazor)
  calling the API over HTTP only—no direct SQL—with pagination and parallel
  fetch performance. Use when building Web pages, dashboard, tables, production
  board, cookie session, or HttpClient auth refresh against NeedleBase.Api.
---

# NeedleBase Web

Oversight tool for owners/managers (and limited tailor views). **No DbContext.** All data via Api HTTP — same boundary as AssetTag Portal.

UI IA/tokens: [needlebase-uiux](../needlebase-uiux/SKILL.md) (sidebar, tables, production board).

## Hard rules

1. **Api:BaseUrl required** — startup should fail if missing. Never hardcode production API hosts.  
2. **Two HttpClients minimum** — `AuthApi` (login/refresh) and `NeedleBaseApi` (bearer + refresh handler).  
3. **Cookie/session** for browser auth; Secure cookies in Production.  
4. **Role-aware nav** — hide Staff/Payments/Reports per [domain](../needlebase-domain/SKILL.md) + uiux IA.  
5. **Offline parity** — do **not** claim full offline like mobile; protect unsaved edits; show sync/pending if local drafts exist.  
6. **Business scope** — send authenticated context; never display cross-business data.  
7. **Auth layout vs app layout** — separate login/register from shell with sidebar.  
8. **Speed** — Server-side pagination/filter for tables; parallel independent API calls with `Task.WhenAll`; defer non-critical dashboard widgets; never download-all-then-filter; keep HttpClients named with sensible timeouts. Full rules: [code-quality-performance](../needlebase-architecture/code-quality-performance.md).  
9. **Inspect before coding** — Follow [engineering-review](../needlebase-architecture/engineering-review.md): inspect first, fix root cause (no parallel workarounds), remove obsolete code, verify with evidence.  

## Tech choice

Pick **one** and stick to it in skills/code:

- **Razor Pages** (closest to AssetTag Portal), or  
- **Blazor** (CSS-friendly for richer production board)

Either way: page/components call typed HTTP services; business rules stay on Api/Application.

## Shell (from UI skill)

Persistent left sidebar: Dashboard, Customers, Measurements, Orders, Production, Payments, Staff, Reports, Settings.  
Top bar: search, SyncChip (connection/API status), profile.

Breakpoints: see [handoff](../needlebase-uiux/handoff.md).

## Page pattern (Razor-style)

```csharp
public class IndexModel : PageModel
{
    private readonly HttpClient _api;
    public IndexModel(IHttpClientFactory factory)
        => _api = factory.CreateClient("NeedleBaseApi");

    public async Task OnGetAsync()
    {
        Orders = await _api.GetFromJsonAsync<List<OrderReadDto>>("api/orders");
    }
}
```

## Services to include

- `ApiAuthService` — login, refresh, current user/business  
- `TokenRefreshHandler` — cookie-backed bearer refresh  
- Unauthorized → redirect to login  

## Local

```bash
dotnet run --project src/NeedleBase.Api --launch-profile https
dotnet run --project src/NeedleBase.Web --launch-profile https
```

Run **both** processes for local dev.

## Related

- [needlebase-api](../needlebase-api/SKILL.md)  
- [needlebase-architecture](../needlebase-architecture/SKILL.md)  
