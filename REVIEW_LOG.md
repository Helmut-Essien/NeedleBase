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
