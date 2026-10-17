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

---

# REVIEW_LOG — Auth UI slice (login/register web + mobile)

## Iteration 1

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

| Severity | Source | Location | Fix |
|----------|--------|----------|-----|
| High | Bugbot | `RegisterPage.xaml` Sign in nav | Restored `Button` with `CanSubmit` / busy guard (no nav during register) |
| High | UI/UX | Mobile field labels | SemanticDescriptions on Entries; removed deprecated `LabeledBy` after XC0618 |
| High | UI/UX | Password helper contrast | Web `--nb-text-secondary` / mobile `Gray600` + 13px |
| High | UI/UX | Sign in tap target | 48px min-height link-style button |
| Medium | Bugbot | Password digit rule | Web `[RegularExpression]` + mobile length/digit check matching helper copy |
| Medium | UI/UX | Confirm password field error | `ConfirmPasswordError` + red border DataTrigger |
| Medium | UI/UX | Web name row / auth scroll | `col-12 col-sm-6`; auth body `flex-start` + `100dvh` |
| Medium | UI/UX | Password hint a11y / focus | `aria-describedby="password-hint"`; footer `:focus-visible` |
| Medium | UI/UX | Field tap height | `MinimumHeightRequest="48"` on field borders |
| Low | UI/UX | Title size / padding / banner | Login title 24; padding 16; removed `role="banner"` |

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Security | (all medium+) | Clean — visual slice; Razor encodes errors; ConfirmPassword not sent to API |
| Security | Google Fonts CDN | Low/defense-in-depth; Inter with system fallbacks; residual note only |
| UI/UX | Card radius 12 vs token 10 | Design screenshots specify 12px — follow design |
| UI/UX | Web button 48 vs token 40 | Design/request specifies 48px auth CTAs |

### Remaining open

None for iteration 1 — proceeded to iteration 2.


## Iteration 2

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

| Severity | Source | Location | Fix |
|----------|--------|----------|-----|
| Medium | UI/UX | `RegisterPage.xaml` ErrorMessage | Moved general errors above form fields |
| Medium | UI/UX | Password field errors | `PasswordError` + red border DataTrigger |
| Medium | UI/UX | Stale confirm/password errors | Cleared in `OnPasswordChanged` / `OnConfirmPasswordChanged` |

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Bugbot | (all) | Clean |
| Security | (all) | Clean |
| UI/UX | Remove CanSubmit from nav buttons | Conflicts with Bugbot High (iter 1): busy-guard prevents mid-request nav + ShowMainAsync race |
| UI/UX | Login ModelState banner ErrorMessage | Pre-existing behaviour; slice keeps validation/error messaging as-is |
| Senior Dev | Google Fonts CDN | Residual privacy note only; Inter + system fallbacks |

### Remaining open

None for iteration 2 — proceeded to iteration 3.


## Iteration 3

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

None — all four reviews clean.

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Senior Dev | Identity default `RequireLowercase` vs helper copy | Pre-existing Identity config (not set false); server fail-safe; not introduced by this visual slice; changing Identity or design copy needs product decision |
| UI/UX | Duplicate password hint when field error shown | Acceptable; helper remains instructional per design |
| Bugbot / Security / UI/UX | (all) | Explicitly clean |

### Remaining open

**None.** Loop complete for auth UI slice: Bugbot, Security, Senior Developer, and UI/UX report zero actionable findings.

### Residual notes for humans

- Google Fonts CDN loads Inter on auth pages (system-font fallbacks present).
- Identity still defaults `RequireLowercase=true` while UI helper mentions length + digit only.
- No automated visual/a11y regression tests for auth screens in this slice.


## Iteration 4

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

| Severity | Source | Location | Fix |
|----------|--------|----------|-----|
| Medium | UI/UX | `RegisterPage.xaml` ErrorMessage | Moved general errors to submit band (above ActivityIndicator) so failures remain visible after scrolling a long form |

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Bugbot | 1 finding removed by validator | Treated as false positive (validator rejected); no remaining validated Bugbot items |
| Security | (all) | Clean — no medium+ |

### Remaining open

None for iteration 4 — re-run suite to confirm clean.


## Iteration 5

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

| Severity | Source | Location | Fix |
|----------|--------|----------|-----|
| Low | UI/UX | Mobile/web password helper duplicate | Hide helper when password field error is active (DataTrigger / `:has` CSS) |

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Bugbot | (all) | Clean |
| Security | (all) | Clean |
| UI/UX | Per-field empty borders for required mobile fields | Pre-existing pattern is single `"Fill in all required fields."`; expanding to per-field empty state changes established validation UX beyond this slice |

### Remaining open

Re-confirm UI/UX clean after helper hide.


## Iteration 5 (confirmation)

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

(See iteration 5 table above for helper-hide; confirmation pass found none new.)

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Bugbot | (all) | Clean |
| Security | (all) | Clean |
| UI/UX | (all) | CLEAN |
| Senior Dev | Identity lowercase / Fonts CDN | Residual notes only; not actionable in this slice |

### Remaining open

**None.** Auth UI slice loop complete.


---

# REVIEW_LOG — Auth UI + nav-bar hide + skills (Ralph loop)

## Iteration 1

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

None — all four reviews clean on first pass of this loop.

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Bugbot | (all) | Clean — nav flags, validation, busy guards OK |
| Security | (all medium+) | Clean — ConfirmPassword not sent; Razor encoded; returnUrl local check unchanged |
| Security | Google Fonts CDN | Residual privacy note only (prior dismissal) |
| UI/UX | (all) | CLEAN — Login/Register page + ShellContent both set `Shell.NavBarIsVisible="False"`; closes duplicate title-bar defect |
| Senior Dev | Safe-area padding after nav hide | No clipping evidence; MAUI/page padding present; residual on-device check only |
| Prior residuals | Identity RequireLowercase vs helper; Inter CDN | Product/privacy notes, not slice regressions |

### Remaining open

**None.** Loop complete: Bugbot, Security, Senior Developer, and UI/UX report zero actionable findings.

### Residual notes for humans

- Redeploy/verify on device that the grey Shell title strip is gone on Login and Register.
- Identity still defaults `RequireLowercase=true` while UI helper mentions length + digit.
- Auth pages load Inter from Google Fonts (system fallbacks in CSS).


## Iteration 2 (confirmation)

**Reviews run:** Bugbot, Security, Senior Developer, UI/UX

### Findings fixed

None — confirmation pass clean.

### Findings dismissed

| Source | Item | Reason |
|--------|------|--------|
| Bugbot | (all) | Clean |
| Security | (all medium+) | Clean |
| UI/UX | (all) | CLEAN — nav hide verified on pages + ShellContent |
| Senior Dev | (all) | Clean — builds succeed; nav flags present; no new infra/perf issues |

### Remaining open

**None.**

