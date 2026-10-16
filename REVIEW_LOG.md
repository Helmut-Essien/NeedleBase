# Review log — Slice 0 scaffold

## Iteration 1

**Date:** 2026-10-16

### Suite results (before fixes)

| Review | Result |
|--------|--------|
| Bugbot | Clean (no bugs) |
| Security | 1 Medium: Android `allowBackup="true"` |
| Senior Developer | Medium: Index cancellation/JSON handling; Error page always showed Dev hints; redundant late `AddEnvironmentVariables`; invalid `Api:BaseUrl` not validated; CA1416 noise on net9.0 TFM |
| UI/UX | High: purple MAUI template vs brand green; Medium: navbar `aria-controls` without matching `id`; missing skip link; scaffold not on design tokens; status colour without live region |

### Fixed

| Severity | Location | Fix |
|----------|----------|-----|
| Medium | `AndroidManifest.xml` | `allowBackup="false"` |
| High | Mobile `Colors.xaml`, Android `colors.xml`, `appicon.svg`, splash/icon csproj | Brand tokens `#2E9E36` / charcoal / app bg |
| Medium | `_Layout.cshtml` | `id="navbarSupportedContent"`, skip link, `main` landmark |
| Medium | `site.css`, Index | Design CSS variables; status with `role="status"` + text |
| Medium | `Index.cshtml.cs` | Rethrow request cancellation; catch `JsonException` |
| Medium | `Error.cshtml(.cs)` | User-facing copy; Dev hints only when Development |
| Medium | `Web/Program.cs` | Absolute http(s) `Api:BaseUrl` validation; remove redundant env reload |
| Low | `Api/Program.cs` | Remove redundant `AddEnvironmentVariables` |
| Low | `Mobile.csproj` | `NoWarn CA1416` for net9.0 test TFM |

### Dismissed

| Finding | Reason |
|---------|--------|
| Anonymous `/health/*` and `/api/test/ping` | Security: intentional probes; minimal JSON; no secrets |
| Vendored Bootstrap/jQuery in wwwroot | Template baseline; no supply-chain finding in pins |
| Displaying `Api:BaseUrl` on home | Ops fingerprint only; not a credential |
| AssetTag still uses `allowBackup=true` | NeedleBase offline-first requires stricter default |

### Remaining open

None for iteration 1 plan — re-run suite next.

### Residual notes

- `/health/ready` remains a healthy stub until Slice 1 SQL.
- Full web sidebar IA and mobile bottom nav deferred to later slices.

---

## Iteration 2

**Date:** 2026-10-16

### Suite results (before fixes)

| Review | Result |
|--------|--------|
| Bugbot | High: `Styles.xaml` still referenced `Magenta` / `MidnightBlue` removed from `Colors.xaml` — app crash on startup |
| Security | Clean (prior mediums resolved; no new medium+) |
| Senior Developer | Same as Bugbot (broken resource graph) — fix required |
| UI/UX | Brand tokens present; pending Magenta→Primary for Shell/Tab styles |

### Fixed

| Severity | Location | Fix |
|----------|----------|-----|
| High | `Styles.xaml` | Replace `Magenta`→`Primary`, `MidnightBlue`→`Charcoal`; Page/Shell bg → `AppBackground` |

### Dismissed

| Finding | Reason |
|---------|--------|
| Security “require HTTPS Api:BaseUrl in Production” | Documented Slice 1+ hardening; http needed for local; deployer-controlled |
| Full sidebar / bottom-nav IA | Out of Slice 0 acceptance; deferred |

### Remaining open

Re-run suite after Styles.xaml fix.

---

## Iteration 3 (final)

**Date:** 2026-10-16

### Suite results

| Review | Result |
|--------|--------|
| Bugbot | Clean — no bugs |
| Security | Clean — no medium+ |
| Senior Developer | Clean — build 0 warnings on Mobile net9.0; health/cancellation/`Api:BaseUrl` hardening verified; Magenta crash fixed |
| UI/UX | Clean for Slice 0 — brand tokens, skip link, navbar `id`, status live region, Error copy; full sidebar/bottom-nav deferred (documented, not actionable for scaffold) |

### Fixed this iteration

None (verification only).

### Dismissed

| Finding | Reason / evidence |
|---------|-------------------|
| Full product IA (sidebar, 5-tab mobile) | Slice 0 acceptance is scaffold only; deferred to later slices per phases.md |
| HTTPS-only Api:BaseUrl in Production | Slice 1+ hardening; http required for local; not exploitable remotely |
| Anonymous health/ping | Intentional; minimal payload; Security + Bugbot agree |
| Vendored Bootstrap/jQuery omitted from review coverage | Third-party static assets; no app logic |

### Remaining open

**None.** All four reviews clean for actionable findings.

### Residual notes for humans

- `/health/ready` is still a healthy stub until Slice 1 wires SQL.
- Prefer committing `REVIEW_LOG.md` with the slice or keep as working notes.
- Android is the shipped MAUI TFM; iOS/Windows stubs unused.

---

# Review log — Slice 1 Auth + Business + PostgreSQL

## Iteration 1

**Date:** 2026-10-16

### Suite results (before fixes)

| Review | Result |
|--------|--------|
| Bugbot | High: `TokenRefreshHandler` reuses `HttpRequestMessage` after send; Medium: case-sensitive login email; Medium: register can orphan Identity user |
| Security | High: JWT/DB secrets in tracked `appsettings.Development.json`; High: refresh rotation race; Medium: logout no server revoke; lockout; rate limit; membership not re-checked; email enumeration; refresh reuse no family revoke |
| Senior Developer | Same as Bugbot on handler retry; Postgres switch OK; secrets/config operability; auth resilience gaps |
| UI/UX | Medium: mobile auth entries placeholder-only (a11y); errors always visible; no busy/disabled affordance; web auth missing skip link / assertive live region |

### Fixed

| Severity | Location | Fix |
|----------|----------|-----|
| High | `TokenRefreshHandler.cs` | Buffer + clone request for transient retry and post-refresh resend |
| High | `appsettings.Development.json`, `DesignTimeDbContextFactory`, README | Remove committed secrets; user secrets / env only; test factory injects keys |
| High | `AuthService.RefreshAsync` | Serializable tx + reuse detection revokes all user refresh tokens |
| Medium | `AuthService.LoginAsync` | `FindByEmailAsync` + Identity lockout (`AccessFailedAsync` / `IsLockedOutAsync`) |
| Medium | `AuthService.RegisterBusinessAsync` | Transaction around Identity create + business/membership; generic register error |
| Medium | `AuthController` + Web/Mobile logout | `POST /api/auth/logout`; clients revoke then clear local session |
| Medium | `Program.cs` | Fixed-window rate limiter policy `auth` on AuthController |
| Medium | `DependencyInjection` JWT events | Active `StaffMembership` check (`IgnoreQueryFilters`) per request |
| Medium | Mobile Login/Register XAML + VMs | Visible labels, SemanticProperties, busy/error visibility, safer error copy |
| Medium | `_AuthLayout`, Login/Register cshtml | Skip link; `aria-live="assertive"` on errors |
| Low | Tests | Case-insensitive login; logout revoke; `NeedleBaseApiFactory` |

### Dismissed

| Finding | Reason |
|---------|--------|
| Email verification (`EmailConfirmed = true`) | No email sender in Slice 1; deferred to staff/invite or polish slice |
| Per-request membership DB hit cost | Correctness over micro-opt; cache later if measured |
| Full web sidebar / mobile 5-tab IA | Out of Slice 1 acceptance; deferred |

### Remaining open

Re-run suite after fixes (iteration 2).

### Residual notes

- Local Postgres: Docker `needlebase-postgres` or any host matching user secrets.
- Set Api user secrets before `dotnet run` (connection string + JWT key).

---

## Iteration 2

**Date:** 2026-10-16

### Suite results (before fixes)

| Review | Result |
|--------|--------|
| Bugbot | High: concurrent Web refresh after lock still posts stale refresh token → reuse revoke-all |
| Security | Medium: refresh tokens plaintext at rest; Medium: Production allows http `Api:BaseUrl` |
| Senior Developer | Agreed with Bugbot concurrent refresh; hashing + HTTPS prod gate are best-practice |
| UI/UX | Clean for Slice 1 auth surfaces after iteration-1 a11y fixes (no new actionable) |

### Fixed

| Severity | Location | Fix |
|----------|----------|-----|
| High | `TokenRefreshHandler.cs` | Process-wide `LatestTokensByUser` cache + re-authenticate after lock before refresh |
| Medium | `RefreshTokenHasher` + `AuthService` | Store SHA-256 hex of refresh tokens; client still receives raw token |
| Medium | `Web/Program.cs` | Require `https` for `Api:BaseUrl` when not Development |

### Dismissed

| Finding | Reason |
|---------|--------|
| Email verification | Still deferred (no mail infra) |
| In-memory token cache multi-instance | Single Web process OK for MVP; sticky sessions / distributed cache later if scaled out |

### Remaining open

Re-run suite (iteration 3).

---

## Iteration 3

**Date:** 2026-10-16

### Suite results (before fixes)

| Review | Result |
|--------|--------|
| Bugbot | High: `LatestTokensByUser` not cleared on logout → can revive prior session tokens |
| Security | Clean (medium+) |
| Senior Developer | Agree with Bugbot cache lifecycle |
| UI/UX | Clean for Slice 1 |

### Fixed

| Severity | Location | Fix |
|----------|----------|-----|
| High | `TokenRefreshHandler` + `Logout.cshtml.cs` | `ClearCachedTokens` on logout and refresh-failure sign-out |

### Dismissed

| Finding | Reason |
|---------|--------|
| Security residual notes (password policy, forwarded headers, DP keys) | Informational / deploy-time; not Slice 1 code defects |

### Remaining open

Re-run suite (iteration 4) to confirm clean.

---

## Iteration 4

**Date:** 2026-10-16

### Suite results (before fixes)

| Review | Result |
|--------|--------|
| Bugbot | High: sign-in does not clear `LatestTokensByUser`; Medium: transient refresh failure signs out Web session |
| Security | Clean (medium+) |
| Senior Developer | Agree — align Web refresh honesty with mobile |
| UI/UX | Clean for Slice 1 |

### Fixed

| Severity | Location | Fix |
|----------|----------|-----|
| High | `AuthCookieSignIn.cs` | `ClearCachedTokens` on sign-in (user id + email keys) |
| Medium | `TokenRefreshHandler.cs` | `RefreshOutcome` — transient → 503 without sign-out; invalid → sign-out |

### Dismissed

None new.

### Remaining open

Re-run suite (iteration 5) for final clean confirmation.

---

## Iteration 5 (final)

**Date:** 2026-10-16

### Suite results

| Review | Result |
|--------|--------|
| Bugbot | Clean — no bugs |
| Security | Clean — no medium+ |
| Senior Developer | Clean — TokenRefreshHandler clone/retry, cache lifecycle, transient honesty verified manually (including handler omitted from some subagent file lists) |
| UI/UX | Clean for Slice 1 — auth labels, semantics, busy/error states, skip link, aria-live; full IA deferred |

### Fixed this iteration

None (verification only).

### Dismissed

| Finding | Reason / evidence |
|---------|-------------------|
| Email verification | Deferred until mail infra |
| Multi-instance token cache | Single Web process MVP; documented |
| Weak password / forwarded headers / DP keys | Deploy / later polish; not Slice 1 defects |
| Full product sidebar/bottom-nav IA | Out of Slice 1 acceptance |

### Remaining open

**None.** All four reviews clean for actionable findings.

### Residual notes for humans

- Configure Api user secrets (`ConnectionStrings:DefaultConnection`, `JwtSettings:SecurityKey`) before local run.
- Local Postgres (e.g. Docker `needlebase-postgres` on 5432).
- Existing plaintext refresh rows (if any from early Slice 1) are invalid after hashing change — re-login.
- When scaling Web horizontally, replace in-memory `LatestTokensByUser` with a shared store or sticky sessions.
