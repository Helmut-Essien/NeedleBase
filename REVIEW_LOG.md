# REVIEW_LOG — Splash screen slice

## Iteration 1

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

| Severity | Source | Location | Fix |
|----------|--------|----------|-----|
| High | Bugbot | `SplashPage.xaml.cs` OnAppearing | One-shot `_startupInProgress` / `_startupCompleted` latch to block overlapping auth/nav |
| Medium | Bugbot | `SplashPage.xaml.cs` EnsureCreated | Dedicated DB failure alert; do not route to login masking storage failures |
| Medium | Senior Dev | `SplashViewModel.cs` | Removed dead ViewModel (unused bindings) |
| Medium | UI/UX | Splash stuck after “Stay here” | Visible “Try again” button + semantic description update |
| Low | Senior Dev / UI/UX | Dot animation | Catch dispose during nav; honor Android animator scale = 0 |
| Low | UI/UX | Splash a11y | Page/logo/dots/retry AutomationId + SemanticProperties |

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Security | DEBUG cleartext HTTP | Dev-only `#if DEBUG`; Release keeps HTTPS — no production exploit path |
| Security | Optional OnAppearing guard | Addressed as Bugbot High above |
| Senior Dev | Always RefreshAsync (no expiry check) | NeedleBase has no `IsTokenExpired`; offline-honest refresh is intentional product rule |
| UI/UX | Parent Helmut logo vs NeedleBase product logo | Explicit product request: splash uses parent-company logo |

### Remaining open

None for iteration 1 — proceeded to iteration 2.

---

## Iteration 2

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

| Severity | Source | Location | Fix |
|----------|--------|----------|-----|
| Medium | Bugbot | `NavigateAwayAsync` | Set `_startupCompleted` only after successful `navigate()` so Try again works if nav throws |
| Low | Senior Dev | Dot animation | Generation counter so retries do not stack overlapping FadeTo loops |
| Low | UI/UX | Stuck → Retry | Reset semantic description to “NeedleBase is starting” when startup restarts |

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Security | (all) | No medium+ issues; prior dismissals still hold |
| Senior Dev | `SetDecorFitsSystemWindows(false)` | Matches AssetTag splash status-bar pattern; not a splash-slice defect |
| UI/UX | splash.svg binary size | Required parent-company logo asset parity with AssetTag |

### Remaining open

None for iteration 2 — proceeded to iteration 3.

---

## Iteration 3

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

| Severity | Source | Location | Fix |
|----------|--------|----------|-----|
| Medium | Bugbot (validator-removed but verified) | `OnAppearing` after Stay here | `_awaitingUserRetry` blocks auto-restart; only Try again clears it |

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Security | (all) | Clean — no medium+ issues |
| Bugbot validator removal | Same Stay-here finding | Validator deferred it; verified against code and fixed anyway |

### Remaining open

None for iteration 3 — proceeded to iteration 4.

---

## Iteration 4

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

None — all four reviews clean.

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Bugbot | splash.svg omitted from automated review | Binary/SVG asset copied from AssetTag parent logo by design; no logic surface |
| Senior Dev | No automated unit tests for SplashPage | Code-behind + MAUI lifecycle; would need device/UI harness — residual note, not a defect in this slice |
| UI/UX | Continuous dots vs skeleton | Matches AssetTag splash pattern; reduced-motion path present |

### Remaining open

**None.** Loop complete: Bugbot, Security, Senior Developer, and UI/UX report zero actionable findings.
