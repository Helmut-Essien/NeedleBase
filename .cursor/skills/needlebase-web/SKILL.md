---
name: needlebase-web
description: >-
  Implements NeedleBase responsive web oversight app (Razor Pages + Bootstrap)
  calling the API over HTTP only—no direct SQL—with pagination and parallel
  fetch performance. Use when building Web pages, dashboard, tables, production
  board, cookie session, or HttpClient auth refresh against NeedleBase.Api.
---

# NeedleBase Web

Oversight tool for owners/managers (and limited tailor views). **No DbContext.** All data via Api HTTP.

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

**Decided: Razor Pages + Bootstrap 5** [code]. Styling via `wwwroot/css/site.css` `--nb-*` variables (see [design-system token map](../needlebase-uiux/design-system.md)). Don't introduce Blazor without a decision.

Either way: page/components call typed HTTP services; business rules stay on Api/Application.

## Shell (from UI skill)

**Built (Slice 6):** persistent left sidebar (Dashboard, Production, Orders, Customers, Payments, Activity). Still planned: Measurements, Staff, Reports, Settings, search, SyncChip.  
**Today:** `_Layout` sidebar from md up, top bar + collapsible menu below md, user name + Sign out, footer Terms/Privacy links. Collapse breakpoint for all nav = `md` (768) + `_AuthLayout` for auth/legal. Shared partials: `_PasswordToggleButton`, `_ValidationScriptsPartial`.  
Responsive, tables→cards, states, copy: [web-screens](../needlebase-uiux/web-screens.md), [content-and-states](../needlebase-uiux/content-and-states.md).

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
        // illustrative: Orders endpoint is planned (S3); today only auth + customers/measurements exist
        Orders = await _api.GetFromJsonAsync<List<OrderReadDto>>("api/orders");
    }
}
```

## Services to include

- `ApiAuthService` — login, refresh, current user/business (the `AuthApi` client adds the browser IP as X-Forwarded-For via `ForwardClientIpHandler`, so the Api's per-IP limits and login throttle see each visitor; a 429 `LOGIN_THROTTLED` shows the shared `AuthErrorMessages` copy)  
- `TokenRefreshHandler` — cookie-backed bearer refresh  
- Unauthorized → redirect to login (401 only; a 403 is a role answer shown on the page, never a sign-out)  
- `INeedleBaseApi` / `NeedleBaseApi` (Slice 6) — typed calls returning `ApiResult<T>`: Ok, Fail(ApiProblem) or Offline (network/timeout/garbled body). Paging clamped to `PaginationDefaults`; query values trimmed + escaped; ids path-escaped. Pages derive from `AppPageModel` (`PageError`, `IsOffline`, `Loaded`; `Fail()` → login on 401) and render `_PageState` for loading/empty/error/offline. Independent calls run with `Task.WhenAll`.  
- `TokenRefreshHandler`: read the token from `ctx.User` (never `AuthenticateAsync` per call — pages fetch in parallel); refresh + sign-out only inside the per-user lock; retry only connection-refused (never replay a timed-out PATCH/POST). Bare 502/503/504 → `ApiResult.Offline`.  
- `UnauthorizedRedirectHandler` only logs; the page redirects on 401 via `AppPageModel.Fail`.  
- Signed-in Razor pages get `Cache-Control: no-store` (middleware, pages only). `UseForwardedHeaders` is the first middleware. Status-code pages re-execute `/Error?code=` (400 = expired form copy).  
- Paging query key is `p` (Razor Pages reserves `page`); `_Pager` partial + `PagerModel`.  
- Role gating: `User.CanSeePayments()` (Owner/Manager) **and** the Api's answer (e.g. dashboard `Money` null) must both allow money. Nav (`_NavLinks`) lists only built pages.  
- Tables use `nb-table nb-table--cards` with `data-label` on each cell (cards below 768px). Status chips: `_StatusChip` partial.  
- Tests: `tests/NeedleBase.Web.Tests` — `FakeApi` for page models; `RenderTests` boots the real app with a test auth scheme to render pages (no network, no data).  
- Api errors are ProblemDetails: read `code`/`detail`/`errors`, map them to page/field messages ([content-and-states §5](../needlebase-uiux/content-and-states.md#5-api-errors-to-user-copy))  
  [code] `ApiAuthService` reads failures via `ApiProblem.ReadAsync` and exposes `LastErrorMessage` (from `AuthErrorMessages`); Login/Register pages show it.  

## Local

```bash
dotnet run --project src/NeedleBase.Api --launch-profile https
dotnet run --project src/NeedleBase.Web --launch-profile https
```

Run **both** processes for local dev.

## Related

- [needlebase-api](../needlebase-api/SKILL.md)  
- [needlebase-architecture](../needlebase-architecture/SKILL.md)

## Activity (22 Oct 2026)

- `/Activity` (Owner/Manager; nav link via `User.CanSeeActivity()`): filters person/kind/from/to/words + hidden `order`/`customer`, `p` paging, 50 per page, `_ActivityItem` partial with a no-JS `<details>` for field changes.
- Order detail loads the last 20 entries in parallel (History panel, “See all” → `/Activity?order=`); a failed history call only hides the panel. Customers list has a History link (`/Activity?customer=`).
- Both HttpClients send `X-NeedleBase-Client: web`; the Api client also forwards the browser IP (`ForwardClientIpHandler`).


## Front-end performance and security rules
See `needlebase-uiux/web-screens.md` § "Web front-end rules": same-origin assets only, CSP-safe (no inline script/style), `asp-append-version` + `defer` on every asset, double-submit guard via `site.js` `data-pending-text`, AA contrast tokens. Note: in Development `MapStaticAssets` sends `no-cache` and uncompressed files, so measure page weight on a publish build.
