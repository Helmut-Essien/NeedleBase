# Slice 3 Ralph review log

## Iteration 1 — 20 Oct 2026

### Reviews run
Bugbot, Security Review, Senior Developer, UI/UX.

### Findings fixed
| Severity | Finding | Fix |
|----------|---------|-----|
| High | CreateOrder `customerId` prefill race | Mirror CustomerEdit `InitializeIfPendingAsync` + Yield |
| High | Customer change left prior garment lines | Clear `Lines` when customer changes |
| High | Status chip missing colour+text+icon | `OrderStatusPresentation` + list/detail chips |
| High | Local `SearchAsync` loaded all orders | Filter status/search in SQL before materialize |
| Medium | Terminal orders still editable via UpdateHeader | Domain guard + API tests |
| Medium | `MeasurementFields` null → NRE | Null-coalesce before serialize |
| Medium | N+1 measurement profile loads | Prefetch profiles once per create |
| Medium | Missing `(BusinessId, CreatedAtUtc)` index | EF + local V2 index |
| Medium | Remove button 40px | Height 48 |
| Medium | Raw `#FEF3C7` overdue tint | `SemanticWarningLight` / `SemanticInfoLight` tokens |
| Medium | No status filter / due+amount on list | Status picker + secondary line |
| Medium | Empty CTA under error | `ShowEmptyAdd` requires `!HasError`; keep cache on load fail |
| Medium | Create lacked client ULID | `_clientOrderId` for idempotent retry |
| Low | FAB text "Order" | "New order" |
| Low | `LoadMeasurementsAsync` uncaught | try/catch + user copy |

### Findings dismissed (with evidence)
| Finding | Reason |
|---------|--------|
| Enforce strict New→…→Collected only | Product: free transitions among non-terminal are intentional for busy tailor shops; terminal lock remains |
| Raw `MeasurementFields` without profile = cross-customer leak | Same-business staff already see all customers; manual fields required when no profile; profile path already CustomerId-bound |
| AssignedToUserId not validated as staff | Deferred Slice 7 (staff invites) |
| Missing dedicated CreateOrder VM unit tests | Covered by local service + API + domain; prefill race fixed by shared CustomerEdit pattern |

### Remaining open
None from iteration 1 verified set. Re-running suite for iteration 2.

### Residual notes
- XamlC XC0022 picker binding warnings (compiled-binding x:DataType on ItemDisplayBinding) — pre-existing style, non-blocking
- On-device visual QA of status chips still recommended

## Iteration 2 — 20 Oct 2026

### Reviews run
Bugbot, Security Review, Senior Developer, UI/UX.

### Findings fixed
| Severity | Finding | Fix |
|----------|---------|-----|
| High | SearchAsync Where on projected DTO not EF-translatable | Filter on entities; OrderBy Id desc + Take in SQL |
| High | Prefill failed outside first 50 customers | GetByIdAsync for PrefillCustomerId then merge list |
| Medium | CreateOrder VM N+1 profile fetch | Rely on OrderLocalService prefetch |
| Medium | Home AttentionItems always empty | Populate overdue/Ready from SearchAsync |
| Medium | Detail line "snapshot cm" | SnapshotLabel: Measurements snapped / No measurements |
| Medium | Update status on terminal orders | CanUpdateStatus hides control |
| Medium | KPI cards not tappable | TapGesture → ViewAllOrders |
| Medium | FAB overlays list | CollectionView Margin bottom 72 |
| Low | Duplicate New header button | Removed; FAB + empty CTA remain |
| Low | Home stage purple/orange mismatch | Align with OrderStatusPresentation tokens |
| Medium | Home overdue chip raw hex | SemanticWarningLight |

### Findings dismissed
| Finding | Reason |
|---------|--------|
| Security: no new issues | Confirmed clean on re-check |

### Remaining open
Re-running suite for iteration 3.

### Residual notes
- Same XamlC XC0022 warnings

## Iteration 3 — 20 Oct 2026

### Reviews run
Bugbot, Security Review, Senior Developer, UI/UX.

### Findings fixed
| Severity | Finding | Fix |
|----------|---------|-----|
| Medium | Attention missed older Ready/overdue | `ListAttentionAsync` SQL filter + Take(5) |
| Medium | Update status visible after failed load | `CanUpdateStatus` requires OrderId + Status |
| Medium | Attention rows not tappable | `OpenCommand` → OrderDetailPage |
| Medium | Attention chip missing icon | StatusIconImage on HomeAttentionItem |

### Findings dismissed
| Finding | Reason |
|---------|--------|
| Security: clean | Explicit no issues |

### Remaining open
Re-running final clean check (iteration 4).

## Iteration 4 — 20 Oct 2026 (clean)

### Reviews run
Bugbot, Security Review, Senior Developer, UI/UX.

### Findings fixed
| Severity | Finding | Fix |
|----------|---------|-----|
| Medium | Ready attention chip white-on-PrimaryLight (~1.1:1) | Bind `StatusBackground` from `OrderStatusPresentation` |

### Findings dismissed
None actionable remaining.

### Final verdict
- Bugbot: no bugs
- Security Review: no issues
- Senior Developer Review: clean
- UI/UX Review: clean

### Residual notes for humans
- XamlC XC0022 on picker `ItemDisplayBinding` (compiled bindings) — cosmetic warnings only
- On-device visual QA of status chips / Home attention still recommended
- Tailor assigned-order limits and `AssignedToUserId` validation remain Slice 7

---

# PlanCatalog (plans step 1) Ralph review log

## Iteration 1 — 25 Nov 2026

### Reviews run
1. Bugbot (subagent) 2. Edge cases & boundaries 3. Security Review (subagent) 4. Senior developer 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| Medium | Edge-input coverage gap for Resolve/ForCode (Unicode lookalikes, ZWSP/NUL, whitespace-only plan, keyless+plan fields, whitespace protected key) | `PlanCatalog.cs:104-172` normalised switch; prior tests only ASCII blanks/`Pro`/`enterprise` (`PlanCatalogTests` before edge cases) | Added tests in `PlanCatalogTests.cs`: lookalike/ZWSP/NUL/emoji fail closed; whitespace plan; keyless ignores plan for entitlements; whitespace protected key stays fail-closed (aligned with `SubscriptionRules.cs:19` `IsNullOrEmpty`) |

### Findings dismissed (with evidence)
| Finding | Severity considered | Why dismissed |
|---------|---------------------|---------------|
| Bugbot: “no bugs” but listed PlanCatalog as unreviwed | — | Untracked files omitted from git-diff-based Bugbot; Security Review + manual Bugbot-equivalent pass covered `PlanCatalog.cs` end-to-end. No logic defects found. |
| Change `IsNullOrEmpty(licenseKeyProtected)` → `IsNullOrWhiteSpace` | Low→Medium (privilege) | Whitespace would become **unlimited** (more privileged). Current `IsNullOrEmpty` matches `SubscriptionRules.cs:19`; `AttachLicense` rejects whitespace (`Business.cs:79`). Test locks the fail-closed behaviour. |
| Wire `WasUnknownPlan` logging now | Low | Skill defers logging to step 2 (`needlebase-plans` PlanCatalog section). Flag exists for callers. |
| Rename README `#pricing-decided-not-built` | Low | Body updated; changing heading would break deep links in `docs/platform-licensing.md` and README TOC. |
| UI/UX issues | — | No UI in this slice (pure Application mapper + unit tests). N/A with evidence. |
| Keyless → unlimited is a bypass | High (security) | Documented product rule; `LicensePlan`/`trial`/`complimentary` only set from ES256-verified Platform checks (`Business.cs:95-101`, Security Review). `"unlimited"` string cannot escalate via `ForCode` (returns null; test). |
| Shared static `PlanEntitlements` instances | Low | Immutable records; O(1) resolve; thread-safe. |

### Remaining open
None verified for iteration 1. Re-run five passes (iteration 2).

## Iteration 2 — 25 Nov 2026 (clean)

### Reviews run
1. Bugbot (subagent, post-commit) 2. Edge cases 3. Security Review (subagent) 4. Senior developer 5. UI/UX

### Findings fixed
None — no verified defects.

### Findings dismissed
| Finding | Evidence |
|---------|----------|
| Bugbot / Security notes on `.cursor/file-backups/` and `.cursor/ralph/scratchpad.md` | Untracked local noise; not in commit `4824e72`; ignored by design |
| Step-2 misuse of `ForCode` alone | Not callable in production today; residual note retained |

### Final verdict
- Bugbot: no bugs
- Edge cases: covered (32 PlanCatalog tests pass)
- Security Review: no medium+ issues
- Senior developer: clean (pure mapper, immutable catalog rows, fail-closed)
- UI/UX: N/A (no UI in slice)

### Residual notes for humans
- Step 2 must call `PlanCatalog.Resolve` (not `ForCode` alone) and combine with `SubscriptionRules` / read-only / `IsClosed`.
- Full `NeedleBase.sln` Release still fails Android **XA0033** (OpenJDK `17.0.21.0.3` version parse) — pre-existing, unrelated.
- Do not commit `.cursor/file-backups/` or `.cursor/ralph/scratchpad.md`.

## Iteration 3 — 25 Nov 2026 (clean)

### Reviews run
1. Bugbot 2. Edge cases 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
None.

### Findings dismissed
| Finding | Evidence |
|---------|----------|
| Untracked `.cursor/file-backups/` / `.cursor/ralph/` | Not in branch commits; local only; ignored |
| No production callers of PlanCatalog | Intentional for step 1; grep shows only `PlanCatalog.cs` + tests |

### Final verdict
- Bugbot: no bugs
- Edge cases: 32 PlanCatalog tests pass
- Security: no medium+ issues (fail-closed, precedence, no secrets)
- Senior: clean (immutable catalog, O(1), no IO/concurrency surface)
- UI/UX: N/A (no UI)

Working tree: clean aside from untracked backups/scratchpad. Slice commits: `4824e72`, `0173f3e` (+ this log).

---

# Staff limits (plans step 2) Ralph review log

## Iteration 1 — 25 Nov 2026

### Reviews run
1. Bugbot 2. Edge cases & boundaries 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| High | Accept-invite resolved plan from pre-transaction `Business` snapshot while counting staff after `FOR UPDATE` | Bugbot + Security: `AuthService.cs` previously loaded business at ~614 then `PlanCatalog.Resolve(business)` after lock without reload; invite/reactivate already reload (`StaffService.cs` 141–143, 274–275). Race: keyless load → attach Starter → accept could still see unlimited | Reload `Business` after business `FOR UPDATE` before `PlanCatalog.Resolve`; use locked entity for tokens (`AuthService.AcceptInviteAsync`) |

### Findings dismissed (with evidence)
| Finding | Why |
|---------|-----|
| Keyless/complimentary unlimited | Product rule; Platform-controlled complimentary; `AttachLicense` only path for keyed plans |
| Owner counted in cap | `CountActiveNonOwnerAsync` / accept count exclude `BusinessRole.Owner` |
| STAFF_LIMIT leaks plan internals | Detail is generic copy only (`StaffErrorMessages.cs`); web `ApiResult.Message` uses Problem detail; mobile `ForProblem` |
| Staff usage meter / upgrade CTA on Staff UI | Step 5 (export and usage display); existing error banner shows STAFF_LIMIT copy (`StaffPage` / web `ActionError` + `role="alert"`) |
| Concurrent race test for stale-plan accept | Existing `Accept_invite_refused_when_plan_staff_cap_already_full` covers post-attach refuse; true interleaving needs timing harness — residual |

### Remaining open
Re-run five passes (iteration 2) after the accept-invite reload fix.

## Iteration 2 — 25 Nov 2026 (clean)

### Reviews run
1. Bugbot 2. Edge cases 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
None — no verified defects after accept-invite reload.

### Findings dismissed
| Finding | Evidence |
|---------|----------|
| Interleaved race harness for attach-during-accept | Row locks + post-lock reload; residual test polish only (Security Review) |
| `.cursor/ralph/done` deleted locally | Not part of staff-limits slice |

### Final verdict
- Bugbot: no bugs
- Edge cases: StaffPlanLimits + 6 PlanLimits API tests + full StaffApiTests (28) pass
- Security: no medium+ issues; accept/invite/reactivate all resolve plan after business `FOR UPDATE`
- Senior: consistent lock + Resolve pattern; Owner-only mutations; O(1) counts under lock
- UI/UX: STAFF_LIMIT detail surfaces via web `ActionError` / mobile `ForProblem`; alert semantics already present; usage meters deferred to step 5

### Residual notes for humans
- Step 5 can show staff usage (e.g. “2/2 staff”) on License/Staff screens
- Optional: timing test for attach-vs-accept interleaving
- Next plan step: Free mode on the phone
- Do not commit `.cursor/ralph/` scratch state

## Iteration 3 — 25 Nov 2026 (clean)

### Reviews run
1. Bugbot 2. Edge cases 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
None.

### Findings dismissed
Same residual notes as iteration 2; no new verified defects.

### Final verdict
All five passes clean. Accept-invite post-lock Business reload confirmed (`AuthService.cs` ~657–672). Plan-limit API tests 6/6 and StaffPlanLimits 9/9 pass.

---

# Unpushed range Ralph (`origin/main..HEAD`) — 25 Nov 2026

Scope: 6 local commits not on origin — PlanCatalog (step 1) + staff caps (step 2).

Commits: `4824e72`, `0173f3e`, `d3dd6bb`, `a9830f7`, `66ec415`, `15ba850` (+ this log).

## Iteration 1 — clean

### Reviews run
1. Bugbot 2. Edge cases 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
None — prior High (stale Business on accept-invite) already fixed in `a9830f7`.

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Keyless/complimentary unlimited | Product rule; Platform-only complimentary (`Business.AttachLicense`) |
| Same-email re-invite blocked at cap | Intentional; invite tx rolls back so no partial revoke (Bugbot) |
| Staff UI usage meters | Step 5; `STAFF_LIMIT` already shown via web `ActionError` / mobile `ForProblem` |
| Attach-vs-accept timing harness | Residual polish; locks + post-lock reload close the bug |

### Final verdict
- Bugbot: no bugs on unpushed implementation
- Edge: PlanCatalog + StaffPlanLimits unit tests (41 filtered) and 6 PlanLimits API tests pass
- Security: no medium+; fail-closed PlanCatalog; invite/accept/reactivate resolve after `FOR UPDATE`
- Senior: shared `StaffPlanLimits` / `PlanCatalog.Resolve`; Owner-only staff mutations; O(1) counts under lock
- UI/UX: no new screens; error copy + existing alert banners adequate for this slice

### Residual notes
- Do not push until you ask
- Next product step: Free mode on the phone
- Do not commit `.cursor/ralph/scratchpad.md`

---

# Free mode (plans step 3) Ralph — 25 Nov 2026

Scope: uncommitted Free phone-only mode (welcome diction, local session, lifetime caps, Save a copy, sync skip).

## Iteration 1

### Reviews run
1. Bugbot 2. Edge cases & boundaries 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
| Severity | Finding | Fix |
|----------|---------|-----|
| Critical | Failed wipe cleared `LocalFreeModeGate` while Free SecureStorage keys remained (`LocalDatabaseService.WipeAsync` finally) | Clear Free gate only when wipe succeeds |
| High | Missing `FreeModeStates` row fail-open (unlimited creates) | `TryReserve*` fails closed when gate active and row missing; AppStartup recreates row from shop name |
| High | Check-then-increment not atomic (crash/TOCTOU undercount) | Atomic SQL `UPDATE … + 1 WHERE count < Max` reserve; release on create failure |
| Medium | More Free usage meters stale on re-appear | Refresh `_freeUsageLabel` when returning to More in Free |
| Medium | Cap path ran for every online create (`_freeMode != null`) | Reserve no-ops unless `LocalFreeModeGate.IsFreeBusiness` |
| Low | FreeSetup Start lacked SR description | `SemanticProperties.Description` on Start button |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Security: Save a copy PII in JSON | User-initiated backup; UI discloses; sanitized cache filename (Security Review) |
| Security: Free/online session confusion | `StartFreeMode` clears tokens; `StoreSession` clears Free; mutually exclusive (Security Review) |
| Double Start Free | `IsBusy` + `IsFreeModeAsync` reject (`FreeSetupViewModel`, `AuthService`) |
| Save a copy large payload | Bounded by 10/20 Free caps |
| Login Free vs invite confusion | Copy separates Free from "Join a shop with an invite" (`LoginPage.xaml`) |
| Shop name max 200 via CustomerName | Matches EF; grapheme vs UTF-16 polish only |

### Tests added/updated
- `FreeModeLocalServiceTests`: missing-row fail-closed, failed-wipe keeps gate, reserve/release
- Migrator latest = 11 (`PaymentSyncTests`)
- Mobile.Tests 333 green; FreePlanLimits 5 green

### Remaining open
Re-run five passes (iteration 2) after these fixes.

### Residual notes
- Pre-existing XA0033 OpenJDK parse warning on full Android Release
- Occasional flaky Api WhatsApp/email timing tests (unrelated; pass on retry)
- Do not push until asked

## Iteration 2

### Reviews run
1. Bugbot 2. Edge cases 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
| Severity | Finding | Fix |
|----------|---------|-----|
| High | AppStartup `CreateAsync` after missing `FreeModeStates` reset counters to 0 | `EnsureAsync` seeds from current local customer/order counts (lower bound of lifetime) |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| True lifetime after FreeModeStates loss when rows were deleted | Unknowable without the counter row; seed from remaining rows is the tightest recoverable floor; deletes still never free slots afterward |
| Security medium+ | None (Security Review); wipe gate / TryReserve SQL / Save a copy / session transitions clean |
| Bugbot validator-removed finding | No surviving medium+ after Ensure seed |

### Tests
- `Ensure_SeedsCountersFromExistingRows_NotZero`
- FreeMode filter: 8/8; Mobile.Tests build green

### Remaining open
None verified. Iteration 3 = confirm clean.

### Residual notes
- Same XA0033 / unrelated Api flakes as iteration 1

## Iteration 3 — clean

### Reviews run
1. Bugbot 2. Edge (Ensure seed verified) 3. Security (iter2 clean held) 4. Senior 5. UI/UX

### Findings fixed
None — Ensure seed from iteration 2 held.

### Findings dismissed
| Finding | Evidence |
|---------|----------|
| Lifetime undercount after deleted rows + lost FreeModeStates | Residual; unknowable; Ensure floors at remaining rows (`EnsureAsync` + test) |

### Final verdict
- Bugbot: CLEAN on Free mode (715b2ee + bf83799)
- Edge: wipe gate, fail-closed, reserve/release, Ensure seed — 8 FreeMode tests green
- Security: no medium+
- Senior: atomic reserve; AppStartup Ensure; DI registered
- UI/UX: welcome diction; More usage refresh; FreeSetup SR description; prior Low closed

### Residual notes
- Do not push until asked
- Next product step: upgrading from Free (plans step 4)
- Pre-existing XA0033 OpenJDK warning; unrelated Api flakes

---

# Unpushed range Ralph (`origin/PlanNameCatalogue..HEAD`) — 25 Nov 2026

Scope: 3 local commits ahead of upstream — Free phone-only mode (plans step 3) + prior Free Ralph hardening/verdict.

Commits: `715b2ee`, `bf83799`, `2caf5bd` (+ this log).

## Iteration 1 — clean

### Reviews run
1. Bugbot 2. Edge cases 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
None — Free Ralph iterations 1–3 already closed wipe-gate, fail-closed reserve, Ensure seed, More usage refresh, diction.

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Lifetime undercount after deletes + lost FreeModeStates | Residual; Ensure floors at remaining rows (`EnsureAsync` + `Ensure_SeedsCountersFromExistingRows_NotZero`) |
| Sync gate vs SecureStorage before startup | Cold start restores gate before `ShowMainAsync`; Free has no tokens for push (Security) |
| PlanCatalog/staff interaction with Free | Free never calls Platform/API; no cross-path defect (Bugbot/Security) |

### Final verdict
- Bugbot: CLEAN on unpushed Free range
- Edge: FreeMode 8/8; FreePlanLimits 5/5; PlanCatalog+StaffPlanLimits filter 46 green
- Security: no medium+
- Senior: priors hold (atomic reserve, Ensure seed, DI)
- UI/UX: welcome diction + Free banner / More Save a copy / Leave Free confirm adequate

### Residual notes
- Do not push until you ask
- Next product step: upgrading from Free (plans step 4)

---

# Free → online upgrade (plans step 4) Ralph — 25 Nov 2026

Scope: working-tree Free upgrade slice (`FreeUpgradeLocalService`, `UpgradeFromFreeAsync` / resume, `FreeUpgradePage`, startup, docs).

## Iteration 1

### Reviews run
1. Bugbot 2. Edge cases & boundaries 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| High | Retry Go online re-calls `register-business` after pending exists | Bugbot; server `FindByEmail` fails (`Infrastructure/Identity/AuthService.cs:85-88`); `UpgradeFromFreeAsync` always POSTed | Short-circuit to `CompleteFreeUpgradeAsync` when pending+tokens (`AuthService.cs`); test `UpgradeFromFree_WhenPendingAndTokens_CompletesWithoutRegisterCall` |
| High | Failed resume falls into Free + re-arms gate despite tokens | Bugbot; `AppStartupService` after `TryResume` false → `IsFreeModeAsync` | `HasPendingFreeUpgradeAsync`; alert+retry while pending remains |
| Medium | Multi-table remap not transactional | Security optional; kill mid-UPDATE leaves mixed BusinessIds | `BeginTransaction` around remap UPDATEs |
| Medium | Password fields lacked show/hide + MaxLength | UI vs `PasswordField` / RegisterPage | `PasswordField` + MaxLength; Remask on disappear |
| Low | Empty `{}` pending treated as valid | Deserialize without JsonException | Validate FromBusinessId/User ids; clear poison blob |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Security medium+ | Security review: parameterized SQL, no secrets in pending, JWT sync tenancy |
| Remap JWT ↔ pending cross-check | Local SecureStorage only; sync uses JWT business (Security) |
| Normal Register from Free could wipe | Pre-existing path; More Go online is the Free path (Security) |

### Remaining open
None from iteration 1 — continued in iteration 2.

## Iteration 2

### Reviews run
1. Bugbot 2. Edge cases & boundaries 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| High | Free flag cleared blocks in-app resume | Bugbot; `UpgradeFromFreeAsync` checked `IsFreeMode` before pending short-circuit; `Complete` clears FreeModeKey after remap | Pending completion first via `TryCompletePendingFreeUpgradeAsync`; Free gate only for new register |
| High | Tokens without pending strands upgrade | Bugbot; StoreTokens then WritePending race; Security residual | Pending blob includes access+refresh; write pending first; restore tokens from blob on resume; tests |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Security medium+ | Security CLEAN on post-iter1 paths; tokens in pending are same-device session secrets already in SecureStorage |
| JWT ↔ pending business_id cross-check | Same-device SecureStorage; sync JWT-scoped (Security residual) |

### Remaining open
Continued in iteration 3.

## Iteration 3

### Reviews run
1. Bugbot 2. Edge cases & boundaries 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| High | Orphan shop before pending write | Bugbot; kill/fail between register-business success and WritePending; normal Login wipes via AcceptOwnerChange | `FreeUpgradeIntentKey` before POST; on `REGISTER_FAILED` + matching intent, login then Complete without wipe; test |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Security: tokens in pending blob | CLEAN — same SecureStorage trust boundary as access/refresh keys |
| JWT ↔ pending cross-check | Same-device; Security residual |

### Remaining open
Continued in iteration 4.

## Iteration 4

### Reviews run
1. Bugbot 2. Edge cases & boundaries 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| Medium | Welcome Sign in with intent still wiped Free rows | Bugbot residual (validator removed as High); `LoginAsync`→`AcceptOwnerChangeAsync` wipe; REGISTER_FAILED copy says sign in | `LoginAsync` remaps via intent+Free without wipe; test `Login_WithFreeUpgradeIntent_RemapsWithoutWipe` |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Security medium+ | CLEAN — intent same-device; login recovery requires valid credentials; sync JWT-scoped |
| Bugbot orphan via Welcome Sign in as High | Validator removed; still closed as Medium above for defense in depth |
| Edge: REGISTER_FAILED for weak password also tries login | Login fails without credentials; no tenancy escape (Security) |
| UI: PasswordField + MaxLength + Remask | Already fixed iter1; terms SemanticProperties; busy/CanSubmit |
| Senior: transactional remap + pending tokens | In place; intent before network is correct crash model |

### Remaining open
None — continued in iteration 5.

## Iteration 5 — clean

### Reviews run
1. Bugbot 2. Edge cases & boundaries 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
None — no new verified High/Medium findings.

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Bugbot new High/Medium | CLEAN — no bugs on upgrade/intent/pending/Login remap/AppStartup/Remap |
| Security medium+ | CLEAN — Login intent remap same-device; JWT sync tenancy |
| Edge: double-submit / concurrent Upgrade | `IsBusy` + pending short-circuit; register email uniqueness |
| Edge: poison pending `{}` | Cleared by validation; tested |
| Senior: crash recovery model | Intent → pending(+tokens) → Complete; transactional remap |
| UI/UX: a11y password / MaxLength / terms | `PasswordField`, MaxLength, SemanticProperties, Remask; busy disable |

### Final verdict
- Bugbot: CLEAN
- Edge: FreeUpgrade 9 tests + Mobile.Tests 343 green; Application Free 5 green
- Security: CLEAN
- Senior: priors hold (intent, pending tokens, transactional remap, no wipe on upgrade)
- UI/UX: Go online form matches code-only auth patterns

### Residual notes
- XA0033 OpenJDK parse warning on Mobile Android build (environment)
- Do not push until asked
- Normal Register (not Go online / not intent login) while Free can still wipe — documented alternate path; More → Go online is the Free upgrade path
- Commits this slice: `85518ae`, `acb3a9f`, `4cb323b`, `1aceb1e` (+ this log)

---

# Unpushed Free-upgrade range Ralph (`origin/PlanNameCatalogue..HEAD`) — 25 Nov 2026

Scope: 5 local commits ahead of upstream (Free→online upgrade + prior Free-upgrade Ralph hardening/verdict).

## Iteration 1

### Reviews run
1. Bugbot 2. Edge cases & boundaries 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| Medium | Stale upgrade intent remaps Free rows into a different signed-in shop | Edge/Senior; Security residual; `LoginAsync` remapped on any Free+intent; bad licence left sticky intent | `FreeUpgradeIntentEmailKey`; Login remap only when email matches; clear intent on non-`REGISTER_FAILED`; tests |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Bugbot High/Medium | CLEAN on AuthService/AppStartup/Remap paths |
| Security medium+ | CLEAN — same-device SecureStorage; JWT sync tenancy |
| UI PasswordField / MaxLength / terms | Already present on `FreeUpgradePage` |
| Normal Register while Free wipe | Documented alternate path |

### Remaining open
None — continued in iteration 2.

## Iteration 2 — clean

### Reviews run
1. Bugbot 2. Edge cases & boundaries 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
None — no new verified High/Medium findings.

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Bugbot High/Medium | CLEAN after intent-email binding |
| Security medium+ | CLEAN; email match closes wrong-shop remap |
| Optional: clear intent on email-mismatch login | Low polish; remap already blocked; cleared on logout/`ClearTokensAsync` (Security residual) |
| Optional: intent == GetBusinessIdAsync on login | Low same-device consistency; Free BusinessId is the intent source |
| Edge: FreeUpgrade 11 + Mobile 345 green | Tests pass |
| UI/UX | Unchanged; prior PasswordField/MaxLength/terms adequate |

### Final verdict
- Bugbot: CLEAN
- Edge: FreeUpgrade 11; Mobile.Tests 345; Application Free 5
- Security: CLEAN
- Senior: intent+email, pending tokens, transactional remap hold
- UI/UX: Go online / More paths unchanged and adequate

### Residual notes
- Do not push until asked
- XA0033 OpenJDK parse warning (environment)
- Normal Register while Free can still wipe — documented alternate path
- Commits this unpushed Ralph: `e10b5f7` (+ this log); prior Free-upgrade commits unchanged

## Export / usage display (plans step 5) — Iteration 1 — 25 Nov 2026

### Slice
Working-tree export/usage: `GET /api/export`, `BusinessExportService`, subscription plan/staff meters, Free Home/More meters, receipt shop phone, web License download, mobile More export.

### Reviews run
1. Bugbot 2. Edge cases & boundaries 3. Security Review 4. Senior developer 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| Medium | More online plan/usage never refreshes on re-appear | Bugbot; `MorePage.OnAppearing` early-return only refreshed Free meters | Keep `_planUsageLabel`; refresh `PlanUsageLine()` on re-appear |
| Medium | Staff meter ignored pending invites (seat reservations) | Bugbot; `StaffPlanLimits.CanAddOne` vs `LicenseService` active-only count | `PendingInviteCount` on DTO; count open invites; web/mobile copy; invalidate license cache on invite/revoke/accept/deactivate/reactivate |
| Medium | Home Free meters showed `0/10` on load failure | Edge/UI; `HomePage.FillFreeMetersAsync` catch | Honest "Usage unavailable right now" + SR description |
| Medium | No API/web tests for export authz/read-only/plan fields | Senior/Security test gap | `LicensingApiTests` export + pending; `LicensePageTests` export; receipt phone share; `ShopExportApi.SafeFileName` |
| Low | Mobile export shown without `CanExport` UI gate | Security residual (server still enforces) | Hide More export when `CanExport == false` |
| Low | Content-Disposition path segment → cache file | Security residual / edge | `ShopExportApi.SafeFileName` + web `SafeExportFileName` |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Security medium+ (authz/tenancy/PII/CSV) | Owner-only + `AllowWhenReadOnly`; JWT business id; JSON not CSV; no license secrets in payload |
| Manager sees export | `StaffRoles.CanManage` is Owner-only |
| `ShopPhoneOnReceipts` not checked on receipt VM | All paid entitlements set `ShopPhoneOnReceipts = true`; Free has no online receipts path |
| Export loads all orders into memory | Acceptable for v1 shop sizes; residual note if Production shops grow huge |
| BusinessExportService in Infrastructure (not Application) | Matches `LicenseService` controller pattern |
| Unicode shop name → stem "shop" | Sanitised stem; Content-Disposition still unique by date |

### Remaining open
None verified High/Medium after fixes. Re-run five passes as iteration 2.

### Residual notes
- Full `NeedleBase.sln` Android TFM still hits XA0033 OpenJDK parse (environment); verify with `-f net9.0` / non-android projects
- Do not push until asked
- On-device QA: More plan line after sync; Free meters; export share sheet

## Export / usage display — Iteration 2 — clean

### Reviews run
1. Bugbot (recheck) 2. Edge 3. Security 4. Senior 5. UI/UX

### Findings fixed
None — no new verified High/Medium findings.

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Bugbot High/Medium | CLEAN on recheck of Ralph fixes |
| Security medium+ | Prior review holds; SafeFileName + CanExport UI are defense-in-depth only |
| Edge: pending cache after invite | Covered by Invalidate + `Subscription_CountsPendingInvites_AsReservedSeats` |
| UI: Home failure meters | Honest copy; More Free path unchanged |

### Final verdict
- Bugbot: CLEAN
- Edge: Application Free/PlanCatalog 39; LicensingApi 24; StaffApi 28; LicensePage 11; Mobile ShopExport+receipt 9
- Security: CLEAN (Owner export, tenancy, no secrets)
- Senior: cache invalidation on seat changes; export tests landed
- UI/UX: plan refresh + pending copy + Free failure honesty

### Residual notes
- XA0033 OpenJDK on full Android TFM (environment)
- Do not push until asked
- On-device QA still recommended for More/Home meters and export share

## Unpushed e96be2a re-review — Iteration 1 — 25 Nov 2026

### Slice
`origin/PlanNameCatalogue..HEAD` = `e96be2a` (export/usage) plus follow-up fix commit.

### Reviews run
1. Bugbot 2. Edge 3. Security 4. Senior 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| Medium | Restored pre-step-5 subscription JSON defaults `CanExport=false`, hiding More export; re-appear did not rebuild License card after sync | Bugbot; `SubscriptionStatusStore` + `bool` default; `MorePage` early-return | `ShouldOfferExport` requires enriched `PlanCode` to deny; `PlanUsageLine` waits when `PlanCode` null; rebuild online slot on every More appear; LicenseGate tests |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Security medium+ | CLEAN — Owner export, tenancy, SafeFileName, no secrets |
| Full-shop export memory | Residual v1; Owner-only self-scoped |
| Web `CanExport` UI | Server always returns enriched DTO (no SecureStorage restore) |
| Receipt phone SR label | Phone is visible companion to shop name; not a blocker |

### Remaining open
None — iteration 2 clean recheck.

## Unpushed e96be2a re-review — Iteration 2 — clean

### Reviews run
Bugbot recheck of CanExport/PlanUsageLine/rebuild; Security residual; Edge tests green; Senior; UI/UX.

### Findings fixed
None.

### Final verdict
- Bugbot: CLEAN
- Edge: LicenseGate 11; LicensingApi 24; ShopExport covered
- Security: CLEAN
- Senior: rebuild + PlanCode sentinel for schema drift
- UI/UX: Export/plan meters honest with stale cache

### Residual notes
- XA0033 OpenJDK on Android TFM (environment)
- Do not push until asked
- On-device: upgrade from pre-export app build, open More before first sync, confirm Export still shown then after sync

## Unpushed e96be2a..0bdebc7 re-review — Iteration 1 — clean — 25 Nov 2026

### Slice
`origin/PlanNameCatalogue..HEAD`: export/usage (`e96be2a`) + CanExport cache fix (`0bdebc7`). Working tree clean aside from Ralph state.

### Reviews run
1. Bugbot (branch + focused MorePage/export files) 2. Edge 3. Security 4. Senior 5. UI/UX

### Findings fixed
None — no new verified High/Medium.

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Bugbot High/Medium | Focused re-read of MorePage/Home/ShopExportApi/BusinessExport/LicenseService/Staff Invalidate: CLEAN; earlier validator-dropped candidate not reproduced |
| Security medium+ | CLEAN — Owner export, tenancy, SafeFileName, CanExport UI fail-open only with API enforcement |
| Uri `new Uri(base, "api/export")` | Same pattern as `StaffApi`/`HttpSyncApi`; `ApiSettings.BaseUrl` defaults with trailing `/` |
| Full-shop export memory | Residual v1 Owner-scoped |
| Home Free meters `0/10` when businessId missing | Broken Free session; catch path already honest; rare |
| Online More rebuild every appear | Intentional for gate refresh; cheap UI |
| Receipt phone without extra SR description | Visible under shop name; not a11y blocker |

### Final verdict
- Bugbot: CLEAN
- Edge: LicenseGate+ShopExport+receipt 20; LicensingApi 24; LicensePage 11; Free/PlanCatalog 39
- Security: CLEAN
- Senior: Invalidate on seat changes; PlanCode sentinel for schema drift hold
- UI/UX: ShouldOfferExport + rebuild + Free failure copy adequate

### Residual notes
- XA0033 OpenJDK on Android TFM (environment)
- Do not push until asked
- On-device: pre-export app → upgrade → More before first sync still shows Export

---

# Paid emails (plans step 6) — Ralph review log

## Iteration 1 — 26 Nov 2026

### Slice
Working-tree diff: PlanEmailService/Worker, OrderService + StaffService hooks, EmailTemplates, Domain watermarks, migration `PaidPlanEmails`, landing copy, tests.

### Reviews run
1. Bugbot (subagent + code re-read) 2. Edge 3. Security (subagent + code re-read) 4. Senior 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| High | Bulk reassign queued up to 2000 `order_assigned` mails; `EmailQueue.Capacity=200` DropWrite → most lost | `StaffService.cs` post-commit loop; `EmailQueue.cs` Capacity=200; Bugbot | `NotifyBulkAssignmentAsync` + `OrdersAssignedBulk` digest (one mail); single-order path unchanged |
| Medium | Insider SMTP / inbox flood via reassign | Security review; same loop | Same digest (also caps volume to 1 mail per reassign) |
| Medium | Queue-then-mark: crash after `TryQueue` before watermark → double-send next pass | `PlanEmailService` due/monthly/weekly paths | Claim watermark first; restore/clear if `TryQueue` fails |
| Low | Assignment queue-full only logged at Debug | `PlanEmailService.NotifyAssignmentAsync` | `LogWarning` when not queued |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| HTML/header injection in paid templates | `H()` / `OneLine` / http(s)-only `Link`; `PlanEmailTests` asserts escaping for monthly/weekly/assigned/due/bulk |
| Tenant leak via `IgnoreQueryFilters` | Predicates always include `BusinessId`; assignee join requires active membership for that shop |
| PII/secrets in logs | Debug/Warning log order/business ids only; `EmailSendingService` already masks recipients; bodies never logged |
| Per-order assignment on create/update | Product: “emails when an order is assigned”; one mail per intentional assign is required; abuse volume is low vs reassign |
| Customer join on `CustomerId` only (due query) | Order/Customer ids are global ULIDs; corrupt cross-tenant id not reachable via API |
| Starter due rows marked without send | Prevents hot re-scan; upgrade same day may miss due mail — accepted tradeoff (documented) |
| Due worker N+1 assignee lookups | Capped by `DueBatchSize=80`; same style as dashboard multi-count; OK for current shop sizes |
| Multi-instance claim race | Deploy is single Api process (MonsterASP); claim-first is sufficient |
| UI/UX a11y on app screens | No new MAUI/Razor interactive screens; landing highlight text only; email HTML is transactional plain layout |
| Landing “coming soon” removal | Covered by `LandingPageTests` |

### Remaining open
None verified High/Medium left in slice.

### Residual notes (humans)
- **Legal:** Privacy/Terms still emphasise invite + password-reset email; paid summaries/assignment/due are now live — update copy when ready (do not invent clauses in this loop).
- Full `NeedleBase.sln` Release can fail XA0033 (OpenJDK version parse) on this machine; non-Android projects build 0 warnings.
- Standing rules: no git commit until the user says `commit`.
- On-device: not applicable for email worker; staging SMTP smoke (monthly/weekly after 06:00 Accra) still recommended.

### Tests re-run
- `PlanEmailTests` (Application): passed
- `StaffApiTests` Assignment / DueMonthly / Reassign bulk digest: passed
- Domain/Application/Infrastructure/Api/Web/Mobile.Data Release `-warnaserror`: passed

### Final verdict (iteration 1)
- Bugbot: CLEAN (after digest + claim-first)
- Edge: CLEAN for verified axes; Starter-upgrade miss dismissed
- Security: CLEAN (after digest)
- Senior: CLEAN for slice scope
- UI/UX: CLEAN (N/A app UI; landing + email templates OK)


## Iteration 2 — 26 Nov 2026

### Reviews run
1. Bugbot (subagent re-review) 2. Edge 3. Security (subagent re-review: CLEAN medium+) 4. Senior 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| High | `DueEmailSentForDate` cleared only when `DueDate` changes, not when assignee changes → new tailor misses day-of due email after reassign/update | `Order.ApplyHeader` lines 217–220; Bugbot; path: mark due sent → `Reassign`/`UpdateHeader` same due → watermark stuck | Clear watermark when assignee changes too; domain + API tests |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Security medium+ after digest | Security re-review CLEAN: one digest per reassign, templates escaped, Owner-only reassign, tenant-scoped memberships, masked logs |
| Owner repeat-reassign digests | Authorized Owner, one mail/call, plan-gated — not cross-tenant escalation |
| UI/UX | Still no new interactive screens in slice |

### Remaining open
None verified High/Medium.

### Residual notes
- Legal Privacy/Terms under-describe paid emails (human)
- No commit until user says `commit`
- XA0033 Android JDK env issue

### Tests re-run
- PlanEmailTests: passed (incl. reassign clears watermark)
- StaffApiTests PlanEmails (5): passed incl. `Reassign_clears_due_watermark_so_new_assignee_gets_due_email`

### Final verdict (iteration 2)
- Bugbot: CLEAN
- Edge: CLEAN (assignee/due watermark axis covered)
- Security: CLEAN
- Senior: CLEAN
- UI/UX: CLEAN


## Iteration 3 — 26 Nov 2026

### Reviews run
1. Bugbot 2. Edge 3. Security (CLEAN medium+) 4. Senior 5. UI/UX  
Prompt step 4 (commit) executed after clean verdict.

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| Medium | Assignment email queued inside sync `InTransactionAsync` before status step; Failed → rollback but SMTP queue kept the mail | `SyncService.PushAsync` + `OrderService.CreateAsync` Notify after SaveChanges; Bugbot | `PlanEmailAssignmentBuffer`: defer when `HasActiveTransaction`; flush after commit in SyncService + OrderService.UpdateAsync; clear on rollback |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Security medium+ | Re-review CLEAN: digest, watermark clear, tenancy, Owner-only reassign |
| UI/UX interactive | Landing highlight strings only; email HTML transactional |
| Edge: empty bulk | `NotifyBulkAssignmentAsync` no-ops on empty; single → NotifyAssignment |
| Edge: email off | `IsConfigured` gates; worker skips |
| Senior: additive migration | Nullable columns + indexes; safe |

### Remaining open
None verified High/Medium.

### Residual notes
- Legal Privacy/Terms still under-describe paid emails
- XA0033 Android JDK on full sln build (env)
- Do not push until asked

### Tests
- PlanEmail* Application: 8 passed (buffer Clear/Flush)
- StaffApiTests PlanEmails: 5 passed
- Commit: local only after this iteration

### Final verdict
Five passes CLEAN after buffer fix; committing slice.


---

# Unpushed commits Ralph review (origin/PlanNameCatalogue..HEAD)

## Iteration 1 — 26 Nov 2026

### Scope
Unpushed: `203f866` Ship paid-plan emails with Ralph hardening (+ follow-up fix commit).

### Reviews run
1. Bugbot (×2, scoped to paid emails) 2. Edge 3. Security 4. Senior 5. UI/UX

### Findings fixed
| Severity | Finding | Evidence | Fix |
|----------|---------|----------|-----|
| Medium | Read-only shops advanced monthly/weekly/due watermarks via `Mark*`, so renewal in the same period never got that summary/due mail | `PlanEmailService` `!MaySendPlanEmails \|\| !Entitlement` → Mark; Bugbot | Skip read-only without marking; only mark when plan lacks entitlement; test `ReadOnly_does_not_advance_monthly_watermark` |

### Findings dismissed (with evidence)
| Finding | Evidence |
|---------|----------|
| Security medium+ on unpushed paid-emails | CLEAN: digest, deferral buffer, tenancy predicates, Owner reassign, template escape |
| Starter due mark-without-send | Still correct — no entitlement; upgrade same-day miss accepted |
| UI/UX | No new interactive UI in unpushed commit |
| Due N+1 assignee lookups | Batch-capped; residual scale only |
| Read-only re-scan every 15m | Correctness over skip; few read-only shops |

### Remaining open
None verified High/Medium.

### Residual notes
- Legal Privacy/Terms under-describe paid emails
- XA0033 Android JDK env
- Do not push until asked

### Tests
PlanEmails Api filter: 6 passed (incl. read-only watermark)

### Final verdict
Five passes CLEAN after read-only watermark fix. Committing follow-up.


# Ralph Loop v4 — Unpushed commits (origin/PlanNameCatalogue..HEAD)

## Iteration 1 — 2026-11-26T08:05:42Z (run_id=20261126T080542Z-285c23)

### Snapshot
```
cmd: git status --porcelain
out:
?? .cursor/ralph/
?? .ralph-20261126T080542Z-285c23/

cmd: git diff --stat origin/PlanNameCatalogue...HEAD
out: 31 files changed, 3352 insertions(+), 37 deletions(-)
```
Dirty outside slice (not owned by this run): `?? .cursor/ralph/` — not stashed/modified (G6). Proceeded with read-only review of committed unpushed range.

### Discovery
- Root: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` ahead of `origin/PlanNameCatalogue` by 2: `e52d98a`, `203f866`
- Slice: `origin/PlanNameCatalogue...HEAD` (paid-plan emails)
- Commit authorization: none (`git config ralph.commit-authorization` unset; `RALPH_COMMIT_AUTHORIZED` unset; prompt does not authorize)
- Postgres: `needlebase-postgres-1` Up on `0.0.0.0:5435->5432/tcp`
- Canonical gates (CI): `dotnet build … -c Release -warnaserror`; `dotnet test … -c Release`; no separate lint tool
- Full solution / Android: XA0033 OpenJDK risk → not used; per-project builds matching CI jobs
- Perf harness: no BenchmarkDotNet in repo for email worker
- Browser/a11y/mobile emulator: not used this iteration

### Gate state table
| gate | state | command | output tail |
|------|-------|---------|-------------|
| build | PASS | `dotnet build tests/NeedleBase.{Application,Api,Web}.Tests/*.csproj -c Release -warnaserror --no-incremental` | `Build succeeded. 0 Warning(s) 0 Error(s)` (each) |
| lint | PASS | same builds with `-warnaserror` (CI has no separate lint) | `0 Warning(s) 0 Error(s)` |
| test | PASS | `dotnet test … --filter PlanEmail\|AssignmentEmail_\|…` + Landing | App 8 passed; Api PlanEmails 6 passed; Web Landing 2 passed; Skipped 0 |
| performance | N/A | classification **B** (background PlanEmailWorker/queries); no email bench harness | `grep BenchmarkDotNet` → no harness; evidence recorded |

Optional:
| gate | state | evidence |
|------|-------|----------|
| a11y (axe/pa11y) | N/A | slice UI change is marketing copy strings only; no axe/pa11y harness in repo |
| security scanner | N/A | Security Review subagent CLEAN medium+; no npm/pip surface in slice |

### Pass ledger
| pass | gate state | tools invoked | findings cited | dismissed with evidence |
|------|------------|---------------|----------------|-------------------------|
| 1 Bugs/control flow | PASS | Bugbot Task; `grep`/`sed` PlanEmailService, Order.ApplyHeader, Sync/OrderService flush | none new | claim-first + restore, buffer flush/clear, due clear on reassign present in e52d98a/203f866 |
| 2 Edge/boundaries | PASS | `sed` PlanEmailService read-only + entitlement paths; buffer source | none | read-only skip-without-mark intentional; Starter mark-without-send accepted product tradeoff |
| 3 Security | PASS | Security Review Task | none medium+ | tenancy, Owner bulk, template escape, MaySendPlanEmails, buffer honesty |
| 4 Senior | PASS | migration Up sed; consumer grep; CI workflow grep; LocalDb Ignore | none blocking | additive nullable columns + indexes; Down present; mobile ignores server-only watermark |
| 5 UI/UX | PASS | LandingSalesInfo sed; LandingPageTests grep | none | copy-only highlights; Landing asserts present; no interactive UI |

### Findings
#### Fixed
None this iteration (no autonomous-fix candidates; Bugbot/Security clean after prior e52d98a).

#### Dismissed
| id | severity | confidence | citation | evidence |
|----|----------|------------|----------|----------|
| D1 read-only watermark abuse | P1 candidate triaged | High | PlanEmailService:216-220,310-314 | Security Review: not abusable; Api test `ReadOnly_does_not_advance_monthly_watermark` |
| D2 Starter mark-without-send same-day upgrade miss | P3 | High | PlanEmailService:223-227 | intentional; endless re-scan avoidance |
| D3 read-only rescans every poll | P4 | Medium | PlanEmailService continue without mark | correctness; batch-capped; not SMTP flood |

#### Open items
None [UNVERIFIED] blocking.

#### Out-of-scope (noted)
- Legal Privacy/Terms under-describe paid emails (docs residual)
- Android full build XA0033 in this environment
- Pre-existing untracked `.cursor/ralph/` (unknown ownership; not touched)

### Commit
Not performed — no commit authorization; no code changes in this iteration.

### Self-audit
- All Fixed/Dismissed have citations
- Pass ledger 5/5 with tools
- Gate states recorded
- Perf N/A with classification B + no harness evidence
- No "Fixed" without citation


### Teardown
```
cmd: hash-verify + unlink owned files under .ralph-20261126T080542Z-285c23/; rmdir namespace
out: (see below)
```
.ralph-20261126T080542Z-285c23/snapshot.txt: deleted
.ralph-20261126T080542Z-285c23/gate_build_app.txt: deleted
.ralph-20261126T080542Z-285c23/gate_build_api.txt: deleted
.ralph-20261126T080542Z-285c23/gate_build_web.txt: deleted
.ralph-20261126T080542Z-285c23/gate_test_app.txt: deleted
.ralph-20261126T080542Z-285c23/gate_test_api_plan.txt: deleted
.ralph-20261126T080542Z-285c23/gate_test_web.txt: deleted
.ralph-20261126T080542Z-285c23/manifest.json: deleted-ns-content
.ralph-20261126T080542Z-285c23/.ralph-owned-20261126T080542Z-285c23: deleted-ns-content
.ralph-20261126T080542Z-285c23: deleted

```
cmd: git status --porcelain (post-teardown)
out:
 M REVIEW_LOG.md
?? .cursor/ralph/
```
- Owned namespace deleted (hash-matched files).
- `.cursor/ralph/` unknown ownership — not deleted (G6).
- `REVIEW_LOG.md` modified by this loop for operator review; commit not authorized.

### Status
**COMPLETE WITH NOTES** — mandatory gates PASS; no blocking findings; optional a11y N/A; residual notes documented; no commit authorization (no code changes).



---

## Ralph v4 — iteration 1 — Workshop step 7a (PDF + customer email + PDF attachment + legal)

**run_id:** `20261126T094025Z-f3852a`  
**slice:** working-tree diff on `PlanNameCatalogue` @ `e52d98a` (PDF receipts, optional Customer.Email, customer email channel, receipt PDF attachment, Privacy/Terms)  
**commit authorization:** none (`ralph.commit-authorization` unset; `RALPH_COMMIT_AUTHORIZED` unset; prompt did not authorize)

### Snapshot
```
cmd: git status --porcelain | head
out: (56 modified tracked + untracked customer-email/receipt/legal files; see discovery)
cmd: git diff --stat HEAD | tail
out: 56 files changed, ~933 insertions(+), 136 deletions(-) (tracked only; untracked extra)
```

### Discovery
- Root: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue`
- Solution: `NeedleBase.sln`; projects Api/Application/Domain/Infrastructure/Web/Mobile/Mobile.Data + 5 test projects
- Canonical verify: CI builds per test project `-c Release --no-incremental -warnaserror`; full `dotnet test NeedleBase.sln -c Release`; Android `net9.0-android` in CI (local JDK parse BLOCKED)
- Runtime: TestDatabase PostgreSQL available for Api.Tests; no mobile emulator; no browser a11y harness; no SMTP real send (CapturingTransport)
- Dependency graph (touched): `EmailMessage` → EmailQueue/SmtpTransport/all templates; `CustomerEmailOutbox` ← SaveChanges interceptor (REST+sync); consent API ← Mobile StaffApi; legal pages ×3 copies

### Pass ledger

| pass | gate state | tools invoked | findings cited | dismissed |
|------|------------|---------------|----------------|-----------|
| 1 Bugs | PASS | Read CustomerEmailOutbox/Interceptor; `dotnet test …CustomerEmailChannelTests` | F1 SMTP-off marks Sent; F2 multi-pay paid sum | — |
| 2 Edge | PASS | Read Outbox paid maps; channel tests TwoPayments/SmtpOff | (same F1/F2) | Queue-full after Sent recorded — P3 residual |
| 3 Security | PASS | Read SmtpTransport/EmailAddresses/Controller auth; diff attachments | F3 MIME name/content-type hardening | Tenancy via query filters — OK |
| 4 Senior | PASS | Read migrations/DI; build/test gates; classify perf B | F1/F2; paid-sum query batch | No EF rollback of Sent without SMTP — fixed by skip Collect |
| 5 UI/UX | PASS | Read CustomerDetailPage/VM; Privacy/Terms pages | — | Emulator screenshots N/A; SemanticProperties present |

### Findings — Fixed

**F1 — P1 High — SMTP off still wrote `CustomerEmailSent` (never queued)**  
`CustomerEmailSaveInterceptor.cs:24-46` — Collect ran whenever SaveChanges ran; Flush skipped when `!IsConfigured`, leaving permanent “sent” rows.  
cmd: `dotnet test …CustomerEmailChannelTests.SmtpOff_DoesNotMarkSent_WhenPaymentRecorded`  
out: `Passed! … Total: 3`  
Fix: skip Collect unless `_email.IsConfigured`.

**F2 — P1 High — multi-payment SaveChanges under-counted paid-so-far**  
`CustomerEmailOutbox.cs` (pre-fix) added only the current Added payment to the DB sum; sync batch of 2 payments → each receipt missed the sibling.  
cmd: `…TwoPaymentsSameSave_PaidSoFarIncludesBoth`  
out: Passed; asserts `Paid so far: GH₵ 500.00` on both.  
Fix: one GroupBy DB sum + sum of all new payments per order.

**F3 — P3 Low — attachment MIME name/type hardening**  
`SmtpTransport.cs` — basename-only filename; reject odd ContentType before `ContentType.Parse`.

### Findings — Dismissed
- Cross-tenant consent: global query filters + RequireManagerOrOwner — OK (code review).
- PDF bytes in EmailQueue: small on-demand PDFs; Capacity 200 — acceptable (P4 note).

### Open / residual
- [UNVERIFIED] TryQueue false (queue full) after Sent committed — rare; no re-send.
- Android `net9.0-android` local build BLOCKED (XA0033 OpenJDK version string) — CI has separate job; Mobile.Tests net9.0 PASS.
- No device/emulator UI proof for consent toggle (code+semantics reviewed).
- No commit (authorization absent).

### Gate state table

| gate | state | command | output tail |
|------|-------|---------|-------------|
| Build (Api.Tests) | PASS | `dotnet build tests/NeedleBase.Api.Tests/… -c Release --no-incremental -warnaserror` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| Build (Web.Tests) | PASS | same pattern Web.Tests | succeeded 0/0 |
| Build (Application.Tests) | PASS | same | succeeded 0/0 |
| Build (Mobile.Tests) | PASS | same | succeeded 0/0 |
| Build (sln + android) | BLOCKED | `dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror` | `XA0033: Failed to get the Java SDK version… openjdk version "17.0.21.0.3"` |
| Lint | PASS | `-warnaserror` on project builds (no separate linter) | 0 warnings |
| Test | PASS | `dotnet test NeedleBase.sln -c Release --no-build` | all Passed; Api 335, Mobile 355, Web 205, App 311, Domain 86; Failed!: 0 |
| Performance | N/A | class **B** (SaveChanges interceptor / sync); no bench harness | reduced N+1 SumAsync → single GroupBy (correctness-driven) |

### Commit
Not performed — no commit authorization.

### Self-audit
- Fixed entries have citations + tests
- Pass ledger 5/5 with tools
- Gate states recorded
- Perf N/A with class B evidence
- Unauthorized scope expansions: none

### Teardown
(see following block after ownership-safe cleanup)

### Status
**COMPLETE WITH NOTES** — runnable mandatory gates PASS; android TFM BLOCKED (env); residual notes; working tree dirty for operator; no commit.


#### Teardown outcomes (iteration 1)
```
cmd: ownership-safe delete of .ralph-20261126T094025Z-f3852a/ (marker match)
out: .ralph-20261126T094025Z-f3852a: deleted
cmd: ls -d .ralph-* 
out: no .ralph-* namespaces
cmd: git status --porcelain (post-teardown)
out: dirty slice remains (PDF/email/legal + REVIEW_LOG); ?? .cursor/ralph/ unknown ownership — not touched (G6)
```


---

## Ralph v4 — iteration 2 — Workshop step 7a (post-fix re-review)

**run_id:** `20261126T105342Z-1ca7be`  
**slice:** same working-tree as iteration 1 (PDF receipts + customer email channel + PDF attachment + legal) on `PlanNameCatalogue` @ `e52d98a`  
**commit authorization:** none

### Snapshot
```
cmd: git status --porcelain | wc -l
out: 74
cmd: git diff --stat HEAD | tail -1
out: 56 files changed, 1035 insertions(+), 136 deletions(-)
```
Extra paths outside product slice: `.cursor/ralph/` (unknown ownership — not touched).

### Discovery
Carried-forward from iteration 1 (no build/lint/CI/dependency config changes; auth still unset). Refreshed snapshot above. Canonical builds remain CI per-test-project `-c Release --no-incremental -warnaserror`; `net9.0-android` still BLOCKED locally (XA0033). No benchmark harness (`find *Bench*` empty).

### Pass ledger

| pass | gate state | tools invoked | findings cited | dismissed |
|------|------------|---------------|----------------|-----------|
| 1 Bugs | PASS | Read Outbox/Interceptor post-F1/F2; `dotnet test …CustomerEmailChannelTests` (4 passed) | — | F1/F2 remain fixed |
| 2 Edge | PASS | Read SyncService SameCustomer/IsStale; Outbox paid maps | — | Old-app Email null wipe — P2 Medium residual (stale-guarded) |
| 3 Security | PASS | Read SmtpTransport hardening; AuditCapture Email mask; Controller policies | — | Tenancy/consent OK |
| 4 Senior | PASS | Migrations list; CI build cmds; class B perf; test integrity | F4 stale XML docs | Queue-full after Sent — residual P3 |
| 5 UI/UX | PASS | Read CustomerEditPage.xaml Email Keyboard+semantics; legal Last updated grep | — | Emulator N/A |

### Findings — Fixed

**F4 — P3 Low High — stale “does not email customers yet” comments**  
`Customer.cs:11`, `CustomerDtos.cs:88/131`, `LocalSchemaMigrator.cs:237` still claimed email was contact-only after the channel shipped.  
cmd: `grep -n "does not email\|Not used to email\|not used for sending" …`  
out: matched those four sites before edit; comments updated to mention opt-in transactional email.

### Findings — Dismissed
- Iter1 F1/F2/F3: re-verified by CustomerEmailChannelTests + template PDF tests (Passed).
- Cross-tenant consent: query filters + RequireManagerOrOwner (code).
- Email Entry a11y: `Keyboard="Email"`, SemanticProperties present (`CustomerEditPage.xaml:75-93`).

### Open / residual
- [UNVERIFIED product] Mixed old/new mobile: Lenient sync with `Email: null` can clear server email if BaseModified allows (SameCustomer + IsStale normally conflict). P2 Medium — no autonomous fix without product rule for “omit vs clear”.
- TryQueue false after Sent committed — rare P3.
- Out-of-scope flakes under parallel Api suite: `EmailQueueTests.FailingMessage…` (695ms > 250ms) and `TailorOfflineCreate_PastNew…` (“order not in pull”); each passes alone; full Api suite green on re-run (335/335).
- Android TFM BLOCKED (XA0033).
- No commit.

### Gate state table

| gate | state | command | output tail |
|------|-------|---------|-------------|
| Build Api/Web/App/Mobile/Domain.Tests | PASS | `dotnet build tests/… -c Release --no-incremental -warnaserror` | `0 Error(s)` each; exits 0 |
| Build net9.0-android | BLOCKED | `dotnet build src/NeedleBase.Mobile/… -f net9.0-android -c Debug -warnaserror` | `XA0033: … openjdk version "17.0.21.0.3"` |
| Lint | PASS | `-warnaserror` on above | 0 warnings |
| Test | PASS | Domain/App/Web/Mobile all green; Api `Passed! … 335` after flake re-run (`/tmp/r2-api-full2.txt`) | Failed!: 0 on final Api run |
| Performance | N/A | class **B**; no harness | evidence: no Bench projects |

### Commit
Not performed — no commit authorization.

### Self-audit
- Fixed/Dismissed cited
- Pass ledger 5/5 with tools
- Gate states recorded
- Perf N/A evidenced
- No unauthorized scope expansion (flake tests not edited)

### Teardown
(see next)

### Status
**COMPLETE WITH NOTES** — mandatory runnable gates PASS; android BLOCKED; residuals documented; no commit.


#### Teardown outcomes (iteration 2)
```
cmd: ownership-safe delete .ralph-20261126T105342Z-1ca7be/
out: .ralph-20261126T105342Z-1ca7be: deleted
cmd: ls -d .ralph-*
out: no .ralph-*
cmd: git status --porcelain | wc -l
out: dirty slice remains; ?? .cursor/ralph/ not touched (G6)
```


---

## Ralph v4 — iteration 3 — full command run (no skipped gates)

**run_id:** `20261126T110239Z-d339f3`  
**slice:** working-tree Workshop 7a (PDF + customer email + attachment + legal)  
**commit authorization:** none (`git config ralph.commit-authorization` exit 1; `RALPH_COMMIT_AUTHORIZED=unset`; prompt does not authorize)

### Snapshot (verbatim commands)
```
cmd: git status --porcelain
out: (74 paths) includes M skills/legal/src/tests + ?? customer-email/receipt files + ?? .cursor/ralph/
cmd: git diff --stat HEAD
out: 56 files changed, 1122 insertions(+), 136 deletions(-)
```
Outside-slice path (not modified by loop): `?? .cursor/ralph/` — unknown ownership; left alone (G6).

### Discovery (full re-run this iteration)
```
cmd: pwd; git rev-parse --show-toplevel; git branch --show-current; git rev-parse HEAD
out: /home/helmut/Documents/My Projects/NeedleBase
     PlanNameCatalogue
     e52d98a498a5d60678dba5fb0b6ec8afb8880b7c
cmd: ls -1 NeedleBase.sln src tests
out: NeedleBase.sln; Api Application Domain Infrastructure Mobile Mobile.Data Web; 5 test projects
cmd: grep standing-rules / CI build lines
out: standing-rules: `dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror`
     CI: per-test-project build+test; also `dotnet build …Mobile.csproj -f net9.0-android -c Debug -warnaserror`
cmd: find …*bench*
out: bench_count=0
cmd: java -version
out: openjdk version "17.0.21.0.3" 2026-10-24 …
cmd: env ConnectionStrings__DefaultConnection
out: unset (Api.Tests uses TestDatabase from factory)
Runtime: TestDatabase available via Api.Tests; no emulator; no browser a11y; no real SMTP.
Dep graph: EmailAttachment/EmailMessage → SmtpTransport/EmailQueue/templates; CustomerEmailOutbox ← SaveChanges interceptor (REST+sync); consent API ← StaffApi; 52 code refs counted.
```

### Pass ledger

| pass | gate state | tools invoked | findings | dismissed |
|------|------------|---------------|----------|-----------|
| 1 Bugs | PASS | `sed` interceptor/outbox; `dotnet test …CustomerEmailChannelTests` | none new | F1/F2 still present in code; tests Passed 3 |
| 2 Edge | PASS | `sed` SameCustomer + Flush; Application template/PDF tests | none new | TryQueue ignore return — residual; sync Email null — residual |
| 3 Security | PASS | `sed` Controller/SmtpTransport/AuditCapture; Domain EmailAddresses tests | none new | RequireStaff/ManagerOrOwner; basename ContentType; Email masked |
| 4 Senior | PASS | `ls` migrations; `find` bench=0; `sed` DI; `grep Skip` on new tests | none new | class B → perf N/A; migrations present; no Skip in new tests |
| 5 UI/UX | PASS | `sed` CustomerDetail/Edit XAML; `grep` legal sync; LegalPagesTests | none new | Keyboard=Email; semantics; legal Last updated 26 Nov; Legal 4 passed |

### Findings — Fixed
None this iteration (no new P0–P2 High).

### Findings — Dismissed (with evidence)
- SMTP-off Collect skip still in place: `CustomerEmailSaveInterceptor.cs:26-44` + channel tests Passed 3.
- Multi-pay paid map still in place: `CustomerEmailOutbox.cs:142-171` + `TwoPaymentsSameSave` covered in channel suite.
- Attachment MIME hardening: `SmtpTransport.cs:44-59`.

### Open items
- Standing-rules / CI android **Build BLOCKED** by XA0033 (OpenJDK version string `17.0.21.0.3` not parsed). Operator must fix JDK tooling or exclude android TFM locally.
- Residual: Lenient sync `Email:null` clear risk (P2 Medium); TryQueue-full after Sent (P3).
- No commit authorization.

### Out-of-scope
- `?? .cursor/ralph/` unknown ownership.

### Gate state table

| gate | state | command | output tail |
|------|-------|---------|-------------|
| Build (standing-rules sln) | **BLOCKED** | `dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror` | `Build FAILED.` `error XA0033: … openjdk version "17.0.21.0.3"` `SLN_EXIT:1` |
| Build (CI Api/Web/Mobile/App/Domain.Tests) | PASS | per-project `--no-incremental -warnaserror` | each `0 Warning(s) 0 Error(s)` exit 0 |
| Build (CI android) | **BLOCKED** | `dotnet build src/NeedleBase.Mobile/… -f net9.0-android -c Debug -warnaserror` | `XA0033` `AND:1` |
| Lint | PASS | `-warnaserror` on CI project builds that executed | 0 warnings |
| Test | PASS | `dotnet test NeedleBase.sln -c Release --no-build` | Domain 86, App 311, Web 205, Mobile 355, Api 335; Failed!: 0; TEST_PIPE:0 |
| Performance | N/A | class **B**; `find *bench*` → 0 | no harness |

**Overall status rule:** mandatory Build from standing-rules is BLOCKED → loop status **BLOCKED** (not COMPLETE).

### Commit
Not performed — no commit authorization.

### Self-audit
- 5/5 passes with tool invocations
- No Fixed claims without citations (none Fixed)
- Perf N/A evidenced
- Gate states recorded including BLOCKED build
- Did not invent substitute build as the sole “PASS”

### Teardown
(see following)

### Status
**BLOCKED** — standing-rules solution build and CI android build cannot execute successfully in this environment (XA0033). Tests and non-android CI project builds PASS. No new code fixes this iteration.


#### Teardown outcomes (iteration 3)
```
cmd: ownership-safe delete .ralph-20261126T110239Z-d339f3/
out: manifest_ok 20261126T110239Z-d339f3
     .ralph-20261126T110239Z-d339f3: deleted
cmd: ls -d .ralph-*
out: ls: cannot access '.ralph-*': No such file or directory
cmd: git status --porcelain | wc -l
out: dirty slice remains (product + REVIEW_LOG); ?? .cursor/ralph/ not touched
```

---

## Ralph Balanced — iteration 1 (2026-11-26)

### Discovery
- Root: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` @ `e52d98a`
- Slice: customer email channel (optional Email, consent, outbox+PDF receipt, legal sync, mobile UI) — 74 dirty paths
- Canonical build (standing-rules): `dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror`
- Canonical CI builds: per test project `-warnaserror`; android TFM `dotnet build … -f net9.0-android -c Debug -warnaserror`
- Bench harnesses: 0 (`find *bench*` empty) → perf N/A for class B
- Runtime: default OpenJDK `17.0.21.0.3` breaks XA version parse; Android Studio JBR `/opt/android-studio/jbr` (`21.0.10`) unblocks android/sln
- Commit authorization: **this prompt** (`commit: authorized`)

### Snapshot
```
cmd: git status --porcelain | wc -l
out: 74
cmd: git diff --stat (tracked)
out: 56 files changed, 1228 insertions(+), 136 deletions(-)  (+ untracked email channel files)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | grep outbox/interceptor; read CustomerEmailOutbox/SaveInterceptor | none new | SMTP-off Collect skip; multi-pay paid map |
| 2 Edge | PASS | grep Sync Email; Customer.Update; TryQueue | Open: Email null clear (P2 Med); TryQueue ignore (P3) | multi-pay GroupBy DB+Added |
| 3 Security | PASS | Authorize policies; AuditMask; Path.GetFileName; ContentType guard | none | consent authz; PII mask; MIME harden |
| 4 Senior | PASS | migrations Up; perf class B; warnaserror builds; tests | none | migrations additive; legal 26 Nov synced |
| 5 UI/UX | PASS | CustomerEdit/Detail XAML SemanticProperties | none | email Entry + consent UI present |

### Gate state table
| gate | state | command | output tail |
|------|-------|---------|-------------|
| Build | PASS | `JAVA_HOME=/opt/android-studio/jbr dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| Lint | PASS | `-warnaserror` on sln + CI project builds | 0 Warning(s) |
| Test | PASS | `dotnet test` Domain/App/Web/Mobile/Api `-c Release --no-build` | Domain 86, App 311, Web 205, Mobile 355, Api 335; Failed 0 |
| Perf | N/A | class B; `find *bench*` → 0 | no harness |

Note: default system JDK yields XA0033; gate PASS requires `JAVA_HOME=/opt/android-studio/jbr` in this environment.

### Findings
**Fixed:** none this iteration (prior slice fixes already in tree).

**Dismissed (with evidence):**
- SMTP-off skips Collect — `CustomerEmailSaveInterceptor.cs:40-44` `"Skip when SMTP is off…"` + `_email.IsConfigured`
- Multi-pay paid-so-far — `CustomerEmailOutbox.cs:142-171` DB GroupBy + paidNewMap
- MIME basename/ContentType — `SmtpTransport.cs:46-62`
- Consent policies — `CustomerEmailController.cs:25,39` RequireStaff / RequireManagerOrOwner
- Legal sync 26 Nov 2026 — Privacy/Terms docs + Web + MAUI

**Open:**
- [UNVERIFIED product] Sync/REST `Email: null` clears stored email via `Customer.Update` (`Customer.cs:46-48`, `CustomerService.cs:221-227`). P2 Medium — needs omit-vs-clear product rule.
- TryQueue return ignored after Sent row (`CustomerEmailOutbox.cs:201`). P3 Low.

**Out-of-scope:** `.cursor/ralph/` unknown ownership — not touched; not committed.

### Teardown
No loop-owned artifacts created this run (no `.ralph-*` namespace). Left working tree for commit.

### Commit
Authorized by Ralph Balanced prompt. Performed after gates PASS and no P0/P1 open.

### Status
**COMPLETE WITH NOTES** — mandatory gates PASS; residual P2/P3 recorded; JDK note for local android builds.

---

## Ralph Balanced — iteration 1 (empty working-tree re-verify) 2026-11-26

### Discovery
- Root: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` @ `46bac03` (ahead of origin by 3)
- Working-tree product slice: **empty** (`git diff` empty; only `?? .cursor/ralph/` sentinel)
- Canonical build: `dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror` (JBR when needed)
- Bench: 0 harnesses
- Commit authorization: this prompt (`commit: authorized`)

### Snapshot
```
cmd: git status --porcelain
out: ?? .cursor/ralph/   (pre-teardown)
cmd: git diff --stat
out: (empty)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | sed interceptor/outbox @ HEAD | none new | SMTP-off Collect skip still present |
| 2 Edge | PASS | sed Customer.Update + CustomerService email | Open carried: Email null clear (P2 Med); TryQueue ignore (P3) — same as prior; not re-fixed (G8/product) | — |
| 3 Security | PASS | CustomerEmailController Authorize; SmtpTransport Path.GetFileName; AuditCapture Email | none | policies + MIME + PII field list |
| 4 Senior | PASS | ls migrations; find bench=0; HEAD already shipped | none | perf N/A (empty WT class C; HEAD class B no harness) |
| 5 UI/UX | N/A | `git diff HEAD -- Views/Pages \| wc -l` → 0 | — | no UI in working-tree diff |

### Gate state table
| gate | state | command | output tail |
|------|-------|---------|-------------|
| Build | PASS | `JAVA_HOME=/opt/android-studio/jbr dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| Lint | PASS | `-warnaserror` on sln build | 0 Warning(s) |
| Test | PASS | Domain/App/Web/Mobile/Api `-c Release --no-build` | 86 / 311 / 205 / 355 / 335; Failed 0 |
| Perf | N/A | empty WT class C; bench count 0 | — |

### Findings
**Fixed:** none (no product diff).
**Dismissed:** prior hardening still at HEAD (SMTP-off Collect; MIME basename).
**Open (carried, not expanded):** Email null omit-vs-clear (P2 Medium); TryQueue after Sent (P3).
**Out-of-scope:** none.

### Teardown
```
cmd: rm -f .cursor/ralph/done && rmdir .cursor/ralph
out: ralph dir removed
cmd: git status --porcelain (post-teardown, pre-REVIEW_LOG append)
out: (clean except REVIEW_LOG append below)
```

### Commit
REVIEW_LOG-only commit if gates PASS (no product changes).

### Status
**COMPLETE WITH NOTES** — empty product slice; HEAD re-verified green; residuals carried; sentinel cleaned.

---

## Ralph Balanced — Reports slice (plan step 7b) 2026-11-26

### Discovery
- Root: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` @ `88b9d14` (tracking origin, in sync at start)
- Slice: profit/monthly reports — 33 dirty paths (Api/App/Infra/Mobile/Web + tests + skills)
- Canonical build: `dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror` (JAVA_HOME JBR)
- Bench: 0 → perf N/A for class B
- Commit authorization: this prompt

### Snapshot
```
cmd: git status --porcelain | wc -l
out: 33
cmd: git diff --stat (tracked)
out: 21 files changed, 96 insertions(+), 22 deletions(-) (+ untracked Reports*)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | read MonthlyReportService/Calculator/ViewModel/Web Index | none High | shared calculator; voided payments excluded; BusinessId filter |
| 2 Edge | PASS | ReportRules.TryResolveMonth; Api tests invalid month; mobile Gate | Open P3: mobile invalid month soft-falls to current (`ReportsViewModel.cs:81-85`) | empty report; EarliestMonth; future month rejected |
| 3 Security | PASS | Authorize RequireStaff + role/plan gates; EscapeDataString month; tenancy test | none | REPORTS_FORBIDDEN/NOT_ALLOWED; no secrets in slice |
| 4 Senior | PASS | PlanCatalog CanReports; migrations N/A (no schema); tests +39 App etc.; perf class B | Open P4: mobile loads all biz orders/payments then filters (`ReportLocalService.cs:35-42`) | additive entitlement only |
| 5 UI/UX | PASS | Reports Index.cshtml a11y; ReportsPage SemanticProperties | none | upgrade prompt; month nav; costs note |

### Gate state table
| gate | state | command | output tail |
|------|-------|---------|-------------|
| Build | PASS | `JAVA_HOME=/opt/android-studio/jbr dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | `-warnaserror` | 0 warnings |
| Test | PASS | Domain/App/Web/Mobile/Api Release --no-build | 86 / 350 / 214 / 365 / 352; Failed 0 |
| Perf | N/A | class B (report queries); bench count 0 | — |

### Findings
**Fixed:** none (no autonomy-eligible defects).
**Dismissed:** role/plan gating + tenancy isolation evidenced by `ReportsApiTests`; calculator empty/trend/voided behaviour by `MonthlyReportTests`.
**Open:** mobile invalid-month soft-fallback (P3); phone full-table load (P4).
**Out-of-scope:** prior email-channel residuals (Email null clear, TryQueue).

### Teardown
No `.cursor/ralph/` present; nothing created by this run to delete.
`git status --porcelain` after commit should be clean of loop artifacts.

### Commit
Authorized; ship reports slice after gates PASS.

### Status
**COMPLETE WITH NOTES** — mandatory gates PASS; P3/P4 residuals only.

---

## Ralph Balanced — unpushed `5de3a78` re-review 2026-11-26

### Discovery
- Slice: `origin/PlanNameCatalogue..HEAD` = `5de3a78` (reports 7b); working tree was clean at start
- Commit authorization: continued Ralph Balanced series (user: “run the loop again on unpushed commits”)

### Snapshot
```
cmd: git status --porcelain
out: (empty at start)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: 5de3a78 Ship profit and monthly reports (plan step 7b).
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | read calculator/service/VM | Fixed P3 soft-fallback (2nd appearance / G8) | voided excluded; BusinessId filter |
| 2 Edge | PASS | TryResolveMonth; Accra Window; tests | Fixed invalid month | empty/future/earliest covered |
| 3 Security | PASS | Authorize + role/plan; EscapeDataString | none | FORBIDDEN/NOT_ALLOWED/tenancy tests |
| 4 Senior | PASS | PlanCatalog reports; no schema; class B | Open P4 phone full-table load (unchanged) | — |
| 5 UI/UX | PASS | Index.cshtml aria; ReportsPage Semantic | none | upgrade + costs note |

### Gate state table
| gate | state | output tail |
|------|-------|-------------|
| Build | PASS | `0 Warning(s) 0 Error(s)` (JBR) |
| Lint | PASS | `-warnaserror` |
| Test | PASS | 86 / 350 / 214 / **366** / 352; Failed 0 |
| Perf | N/A | class B; bench 0 |

### Findings
**Fixed:** `ReportsViewModel.cs:81-88` — invalid month → `MonthInvalidMessage`, no silent current-month load; test `Load_invalid_month_shows_message_without_building`.
**Dismissed:** authz/tenancy/calculator behaviours already covered by unpushed tests.
**Open:** P4 `ReportLocalService.cs:35-42` full-table load (no harness; not auto-fixed).
**Out-of-scope:** email-channel residuals.

### Teardown
No `.cursor/ralph/` present.

### Status
**COMPLETE WITH NOTES** — P3 closed; P4 residual; gates PASS.

---

## Ralph Balanced — unpushed re-review #2 (`5de3a78`+`4b42d6a`) 2026-11-26

### Discovery
- Slice: `origin/PlanNameCatalogue..HEAD` (ahead 2); working tree clean
- Auth: continued balanced series (“run the loop again on unpushed commits”)

### Snapshot
```
cmd: git status --porcelain
out: (empty)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: 4b42d6a Reject invalid report months…
     5de3a78 Ship profit and monthly reports…
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | sed VM + MonthlyReportService | none new | P3 invalid-month fix verified at VM:81-88 |
| 2 Edge | PASS | month input; StillOwed `left > 0` | Open P3 Low: web `<input type=month>` has min, no max (Index.cshtml:44) — future picks redirect | overpay excluded from StillOwed |
| 3 Security | PASS | Authorize/BusinessId; EscapeDataString month | none | — |
| 4 Senior | PASS | ReportLocalService load path | **G8 STOP**: P4 full-table load 3rd appearance (`ReportLocalService.cs:35-42`) — not re-fixed; operator decision needed for optimization | class B perf N/A |
| 5 UI/UX | PASS | aria/Semantic hit count 20 | none | — |

### Gate state table
| gate | state | tail |
|------|-------|------|
| Build | PASS | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | `-warnaserror` |
| Test | PASS | 86 / 350 / 214 / 366 / 352; `Load_invalid_month` Passed 1 |
| Perf | N/A | class B; bench 0 |

### Findings
**Fixed:** none this iteration (P3 already in `4b42d6a`).
**Dismissed:** prior authz/calculator coverage; invalid-month path confirmed.
**Open:** P4 phone full-table load (G8 stop — same finding unresolved across loops). P3 Low: month input missing `max` (server still rejects).
**Out-of-scope:** email-channel residuals.

### Teardown
No `.cursor/ralph/`.

### Operator note (G8)
Further Ralph loops on this unpushed range will keep seeing P4 unless you choose to:
1. Accept as-is (SQLite DateTimeOffset limits; documented), or
2. Authorize a scoped optimization (e.g. fetch window in memory after coarser SQL filter, or accept current).

### Status
**COMPLETE WITH NOTES** — gates PASS; G8 stop on P4; no product commit.

---

## Ralph Balanced — unpushed re-review #3 2026-11-26

### Discovery
- Slice: `origin/PlanNameCatalogue..HEAD` (was ahead 3: 5de3a78, 4b42d6a, 7d3b800); WT clean at start
- Auth: balanced series continued

### Snapshot
```
cmd: git status --porcelain
out: (empty at start; then Index.cshtml(+max) + Index.cshtml.cs + ReportsPageTests)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD (pre-fix)
out: 7d3b800 / 4b42d6a / 5de3a78
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | sed VM invalid-month; ReportLocalService | none new | P3 month fix still present |
| 2 Edge | PASS | Index.cshtml month input | **Fixed** P3 Low: add `max=@Model.LatestMonthKey` | — |
| 3 Security | PASS | name-only Reports* in unpushed | none | authz unchanged |
| 4 Senior | PASS | ReportLocalService lines | P4 full-table **G8 carried** (not re-opened for fix) | class B N/A |
| 5 UI/UX | PASS | aria counts; max on picker | Fixed picker upper bound | — |

### Gate state table
| gate | state | tail |
|------|-------|------|
| Build | PASS | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | `-warnaserror` |
| Test | PASS | 86 / 350 / 214 / 366 / 352 |
| Perf | N/A | class B; bench 0 |

### Findings
**Fixed:** `Index.cshtml` month `max` + `IndexModel.LatestMonthKey` (Accra this month); render test asserts max.
**Dismissed:** prior reports authz/math.
**Open:** P4 phone full-table load (G8 — accept unless operator authorizes optimize).
**Out-of-scope:** email residuals.

### Teardown
No `.cursor/ralph/`.

### Status
**COMPLETE WITH NOTES** — P3 max fixed; P4 residual; gates PASS.

---

## Ralph Balanced — unpushed re-review #4 2026-11-26

### Discovery
- Slice: ahead 4 (`5de3a78`…`ddae3fd`); WT clean at start
- Auth: balanced series

### Snapshot
```
cmd: git status --porcelain
out: (empty → then ReportsController audit CT)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD (pre-fix)
out: ddae3fd / 7d3b800 / 4b42d6a / 5de3a78
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | OnAppearing/Go; audit WriteAsync | **Fixed** P3: pass `cancellationToken` to audit (match SyncController) | LoadAsync already sets Message |
| 2 Edge | PASS | max/LatestMonthKey; IsBusy | none new | prior month max present |
| 3 Security | PASS | Authorize/BusinessId/Escape | none | — |
| 4 Senior | PASS | ReportLocalService | P4 G8 carried (no optimize) | class B N/A |
| 5 UI/UX | PASS | aria + max= | none | — |

### Gate state table
| gate | state | tail |
|------|-------|------|
| Build | PASS | `0 Warning(s) 0 Error(s)` (post-fix rebuild) |
| Lint | PASS | `-warnaserror` |
| Test | PASS | 86 / 350 / 214 / 366 / 352 |
| Perf | N/A | class B; bench 0 |

### Findings
**Fixed:** `ReportsController.cs:50-52` — `cancellationToken` on audit WriteAsync.
**Dismissed:** prior P3 invalid-month + month max still present.
**Open:** P4 phone full-table load (G8 — not re-fixed).
**Out-of-scope:** email residuals; OnAppearing silent Debug catch (P4, matches other pages).

### Teardown
No `.cursor/ralph/`.

### Status
**COMPLETE WITH NOTES** — gates PASS; audit CT fixed; P4 residual.

---

## Ralph Balanced — unpushed re-review #5 2026-11-26

### Discovery
- Slice: ahead 5 (`5de3a78`…`b346ee9`); WT clean
- Auth: balanced series

### Snapshot
```
cmd: git status --porcelain
out: (empty)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: b346ee9 / ddae3fd / 7d3b800 / 4b42d6a / 5de3a78
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | PaidToDate server vs phone | none new | semantics aligned |
| 2 Edge | PASS | month parse InlineData; entitlement keyless | none new | prior fixes present |
| 3 Security | PASS | Authorize/CanView/CT/Escape | none | — |
| 4 Senior | PASS | ReportLocalService | P4 G8 carried | class B N/A |
| 5 UI/UX | PASS | aria/max counts; no Html.Raw/style | none | — |

### Gate state table
| gate | state | tail |
|------|-------|------|
| Build | PASS | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | `-warnaserror` |
| Test | PASS | 86 / 350 / 214 / 366 / 352 |
| Perf | N/A | class B; bench 0 |

### Findings
**Fixed:** none (no new autonomy-eligible defects).
**Dismissed:** prior Ralph fixes verified in tree; PaidToDate cross-client aligned.
**Open:** P4 phone full-table load only (G8 — stopped for fix without operator auth).
**Out-of-scope:** OnAppearing Debug-only catch (P4 polish).

### Teardown
No `.cursor/ralph/`.

### Operator note
Unpushed range is gate-clean. Further Ralph loops without new commits or a decision on P4 will only re-verify green + restate G8.

### Status
**COMPLETE WITH NOTES** — gates PASS; no product commit; P4 residual.

---

## Ralph Balanced — G13 Home Payment visibility 2026-11-26

### Discovery
- Root: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` @ `45dcc03` (ahead 1 docs); WT slice = G13 Home Payment fix (10 paths)
- Auth: this prompt (`commit: authorized`)
- Bench: 0 → perf N/A (class C: visibility/nav guard)

### Snapshot
```
cmd: git status --porcelain | wc -l
out: 10
cmd: git diff --stat
out: 10 files changed, 151 insertions(+), 21 deletions(-)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | read HomeViewModel/HomePage; git diff | none High | early return + ArgumentException catch; shared CanRecordPayments |
| 2 Edge | PASS | tests Tailor/Owner/null role; missing route | Open P3: default CanManagePayments=true until Load (brief Tailor flash) | null role ⇒ show (matches tab) |
| 3 Security | PASS | role gate = PaymentRules.CanManage | none | no secrets; nav guarded |
| 4 Senior | PASS | Singleton HomePage/VM; tests +11; G13 docs | none | no schema; class C |
| 5 UI/UX | PASS | IsVisible + ApplyQuickActionColumns 4→3; SemanticProperties kept | none | 3 equal columns for Tailor |

### Gate state table
| gate | state | tail |
|------|-------|------|
| Build | PASS | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | `-warnaserror` |
| Test | PASS | 86 / 350 / 214 / **377** / 352; HomeViewModel filter Passed 12 |
| Perf | N/A | class C |

### Findings
**Fixed:** none this loop (slice already implements G13).
**Dismissed:** crash path closed by visibility+guard+tests (`HomeViewModel.cs:310-326`, `HomePage.xaml:114`, `HomePage.xaml.cs:28-41`).
**Open:** P3 brief Payment flash for Tailor before Load (default true).
**Out-of-scope:** G14/G15; reports P4.

### Teardown
No `.cursor/ralph/`.

### Status
**COMPLETE WITH NOTES** — gates PASS; ship G13 WT; P3 flash noted.

---

## Ralph Balanced — unpushed G13 re-review 2026-11-26

### Discovery
- Slice: `origin/PlanNameCatalogue..HEAD` was `45dcc03`+`2df46e0`; WT clean at start
- Auth: balanced series (“run the loop again on unpushed commits”)

### Snapshot
```
cmd: git status --porcelain
out: (empty at start)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: 2df46e0 Hide Home Payment…
     45dcc03 Docs: record J2–J5…
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | sed RecordPaymentAsync; AppShell CanRecordPayments | none new | guard+catch present |
| 2 Edge | PASS | prior P3 flash (2nd appearance) | **Fixed** default `canManagePayments` false until Load | null-role tab rule unchanged |
| 3 Security | PASS | role gate = PaymentRules | none | — |
| 4 Senior | PASS | class C; tests +1 | none | — |
| 5 UI/UX | PASS | IsVisible + columns | Fixed no Tailor Payment flash | — |

### Gate state table
| gate | state | tail |
|------|-------|------|
| Build | PASS | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | `-warnaserror` |
| Test | PASS | 86 / 350 / 214 / **378** / 352; HomeViewModel Passed 13 |
| Perf | N/A | class C |

### Findings
**Fixed:** `HomeViewModel.cs` — `canManagePayments` defaults false; test `CanManagePayments_StartsHidden_UntilLoad`.
**Dismissed:** G13 crash path still covered by unpushed tests.
**Open:** none blocking.
**Out-of-scope:** G14/G15.

### Teardown
No `.cursor/ralph/`.

### Status
**COMPLETE WITH NOTES** — gates PASS; P3 flash closed.

---

## Ralph Balanced — unpushed clean-stop 2026-11-26

### Discovery
- Slice: `45dcc03` / `2df46e0` / `5657544` (ahead 3); WT clean
- Auth: balanced; operator asked to loop until no changes remain

### Snapshot
```
cmd: git status --porcelain
out: (empty)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: 5657544 Keep Home Payment hidden until the role is loaded.
     2df46e0 Hide Home Payment for Tailors…
     45dcc03 Docs: record J2–J5…
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | read HomeViewModel RecordPayment + default | none | early return; ArgumentException catch; default false |
| 2 Edge | PASS | StartsHidden test; role theory | none | Tailor/Owner/Manager covered |
| 3 Security | PASS | git diff secrets hunt | none | role gate only |
| 4 Senior | PASS | build/test; class C | none | G14/G15 out of slice |
| 5 UI/UX | PASS | IsVisible + columns + Semantic | none | — |

### Gate state table
| gate | state | tail |
|------|-------|------|
| Build | PASS | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | `-warnaserror` |
| Test | PASS | 86 / 350 / 214 / 378 / 352 |
| Perf | N/A | class C; bench 0 |

### Findings
**Fixed:** none (no autonomy-eligible defects left on this range).
**Dismissed:** G13 complete; fail-closed default verified (`canManagePayments` field default false).
**Open:** none in-slice.
**Out-of-scope:** G14/G15; historical reports P4.

### Teardown
No `.cursor/ralph/`.

### Stop condition
No product changes this iteration. Further loops on the same unpushed range without new commits would only re-verify green.

### Status
**COMPLETE** — mandatory gates PASS; no blocking or in-slice open findings; no product commit.

---

## Ralph Balanced — G14 order assignment 2026-11-26

### Discovery
- Branch: `PlanNameCatalogue` @ `94bb251` (synced with origin); WT = G14 assign (28 paths)
- Auth: this prompt (`commit: authorized`)
- Bench: 0 → perf N/A (class B order update)

### Snapshot
```
cmd: git status --porcelain | wc -l
out: 28
cmd: git diff --stat (tracked)
out: 23 files changed, 363 insertions(+), 25 deletions(-) (+ untracked assign tests/StaffDirectory)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | read UpdateAssigneeAsync; OrderEditor.Apply | **Fixed P1/P2**: header pre-read outside lock could clobber concurrent notes/due (`OrderService.cs` UpdateAssigneeLockedAsync) | assignee reuses edit rules |
| 2 Edge | PASS | Api Assign tests; former member option; Free hides CanAssign | none open | unassign null; stale 409 |
| 3 Security | PASS | RequireStaff + CanEdit; write guard (no AllowWhenReadOnly); staff cache cleared on logout | none | cross-shop 404; inactive ASSIGNEE_INVALID |
| 4 Senior | PASS | no schema; PATCH /assignee; tests Api/App/Web/Mobile | none | class B N/A |
| 5 UI/UX | PASS | web assign label/aria; mobile CanAssign picker | none | former-member option |

### Gate state table
| gate | state | tail |
|------|-------|------|
| Build | PASS | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | `-warnaserror` |
| Test | PASS | 86 / 356 / 225 / 384 / 357; Assign filter Passed 12 |
| Perf | N/A | class B; bench 0 |

### Findings
**Fixed:** `OrderService.UpdateAssigneeAsync` — read due/fitting/notes under row lock (`UpdateAssigneeLockedAsync`) so assign cannot overwrite a concurrent header edit without BaseModifiedUtc.
**Dismissed:** role/tenancy/stale/email covered by `StaffApiTests.Assign`.
**Open:** none blocking.
**Out-of-scope:** G15.

### Teardown
No `.cursor/ralph/`.

### Status
**COMPLETE WITH NOTES** — gates PASS; lock fix shipped with G14.

---

## Ralph Balanced — unpushed `8f2c5ab` re-review 2026-11-26

### Discovery
- Slice: `origin/PlanNameCatalogue..HEAD` = `8f2c5ab` (G14); WT clean
- Auth: balanced series

### Snapshot
```
cmd: git status --porcelain
out: (empty)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: 8f2c5ab Ship order assignment (G14) with assignee update under the row lock.
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | sed UpdateAssigneeLockedAsync | none new | lock-then-read header present; double LockOrder same txn OK |
| 2 Edge | PASS | Assign Api/Mobile/Web tests | none | tailor/inactive/stale/sync/unassign |
| 3 Security | PASS | Authorize; EscapeDataString assignee PATCH; staff cache clear | none | — |
| 4 Senior | PASS | class B; gates | none | prior lock fix holds |
| 5 UI/UX | PASS | assign label/aria; Picker Semantic | none | — |

### Gate state table
| gate | state | tail |
|------|-------|------|
| Build | PASS | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | `-warnaserror` |
| Test | PASS | 86 / 356 / 225 / 384 / 357 |
| Perf | N/A | class B; bench 0 |

### Findings
**Fixed:** none.
**Dismissed:** lock fix verified; authz/edge coverage intact.
**Open:** none in-slice.
**Out-of-scope:** G15.

### Teardown
No `.cursor/ralph/`.

### Status
**COMPLETE** — gates PASS; no product changes.

## Ralph Balanced — G15 polish (PlanNameCatalogue WT) — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` tracking `origin/PlanNameCatalogue`
- Slice: uncommitted G15 batch (UK licence prose, spaced phones, sync live-reload, Staff refresh, password a11y, docs)
- Build: `JAVA_HOME=/opt/android-studio/jbr dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror`
- Lint: no separate `dotnet format` gate (code-gaps); `-warnaserror` on Release build is the lint stand-in
- Test: `dotnet test tests/NeedleBase.{Domain,Application,Web,Mobile,Api}.Tests -c Release --no-build` (Postgres `docker compose` :5435)
- Perf harness: none for this slice
- Commit auth: yes (RALPH BALANCED prompt: `commit: authorized`)
- Runtime: JBR present; postgres started for Api tests

### Snapshot
```
cmd: git status --porcelain
out: 37 modified + 3 untracked G15 test files (+ .cursor/ralph/ scratch this run)
cmd: git diff --stat
out: 37 files changed, 185 insertions(+), 60 deletions(-)
```

### Pass ledger
| pass | state | tools used | findings | dismissals |
|------|-------|------------|----------|------------|
| 1 Bugs | PASS | Read SyncEngine/OrderDetail/PaymentReceipt/Staff; `dotnet test` G15 filters | none P0–P2 | Push `NotifyChangedBySync` after commit (`SyncEngine.cs:422-427`); OrderDetail keeps raw `CustomerPhone` for WhatsApp (`OrderDetailViewModel.cs:363-364`); CustomerDetail `Phone` is display Label only |
| 2 Edge | PASS | FormatPhone + PolishG15Tests UK/non-Ghana; LiveReload IsBusy/picker guard | none | FormatPhone idempotent on spaced Ghana; UK phone left as entered (`PolishG15Tests` passed) |
| 3 Security | PASS | PasswordSemanticsG15Tests; Ui.Entry password branch; tel/search keep raw E.164 | none | Password Entry no longer exposes `description, value` (`LoginPage.xaml` Description removed; `Ui.cs` skips for password) |
| 4 Senior | PASS | class C; Release build+full tests; G15 tests | P3 doc: `LicenseErrors.cs:70` XML still says "license" | Not user-facing; record only |
| 5 UI/UX | PASS | RenderTests spaced phone; Staff RefreshView; Decorative icons; date Format | none blocking | TalkBack listen-through / G15d/h remain manual (code-gaps) |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | `JAVA_HOME=…/jbr dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| Lint | PASS | same build `-warnaserror` (no format gate) | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api `-c Release --no-build` | 86 / 368 / 226 / 391 / 357; Failed 0 |
| Perf | N/A | class C (display FormatPhone + existing debounced live refresh) | no harness |

### Findings
**Fixed:** none this run (slice already implemented; review verified).
**Dismissed:**
- CustomerDetail formatted `Phone` mutating storage — display-only Label; edit navigates to CustomerEditPage (`CustomerDetailViewModel.cs:356`, `EditAsync` → CustomerEditPage).
- WhatsApp/call break from spaced phone — OrderDetail keeps raw `CustomerPhone` (`OrderDetailViewModel.cs:363`) and binds display separately (`OrderDetailPage.xaml:37`).
- Web search/tel corruption — Details uses FormatPhone for text, raw for `asp-route-search` (`Details.cshtml`; `RenderTests.Details_shows_spaced_phone…` passed).
**Open (non-blocking):**
- P3: `LicenseErrors.Phrase` XML doc still quotes US "license" (`LicenseErrors.cs:70`) while runtime copy uses "licence".
- Manual residual (code-gaps G15): d, g-TalkBack listen-through, h.
**Out-of-scope:** `CustomerDetailPage.xaml` phone Description hiding the number (already noted in code-gaps).

### Teardown
Removed `.cursor/ralph/` (scratchpad from this run). No other sentinels.

### Status
**COMPLETE WITH NOTES** — mandatory gates PASS; no P0/P1; manual TalkBack/journey items and one XML-doc P3 remain.

## Ralph Balanced — G15 unpushed re-verify — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` ahead 1 of `origin/PlanNameCatalogue`
- Slice: `origin/PlanNameCatalogue..HEAD` = `28f67b7` (G15 polish)
- WT clean at start (except this run's `.cursor/ralph/`)
- Build/lint/test: standing-rules Release `-warnaserror`; full test projects; Postgres :5435 up; JBR
- Commit auth: yes (user: run the loop again on unpushed commits)

### Snapshot
```
cmd: git status --porcelain
out: (clean product tree)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: 28f67b7 Ship G15 polish: UK licence copy, spaced phones, sync live reload, password a11y.
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | `git show` SyncEngine/OrderDetail/PaymentReceipt; G15 mobile filters | none new | `_staffRefreshed` OK: `OrderDetailViewModel` is Transient (`MauiProgram.cs:124`); Disappear detaches live refresh |
| 2 Edge | PASS | PolishG15 UK/non-Ghana; LiveReload IsBusy/picker | none | FormatPhone + spaced/search split still hold |
| 3 Security | PASS | Login/Register password Entries; PasswordSemanticsG15 (2); Ui password skip | none | password Entry has no SemanticProperties.Description |
| 4 Senior | PASS | class C; fresh Release gates | P3 only: `LicenseErrors.cs:70` XML "license" | doc-only; not user-facing |
| 5 UI/UX | PASS | RenderTests spaced phone; Staff refresh tests | none | manual TalkBack/d/h still open per code-gaps |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | `JAVA_HOME=…/jbr dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api `-c Release --no-build` | 86 / 368 / 226 / 391 / 357; Failed 0 |
| Perf | N/A | class C | no harness |

### Findings
**Fixed:** none.
**Dismissed:** prior G15 dismissals re-checked; Transient VM makes `_staffRefreshed` once-per-screen safe.
**Open:** P3 XML doc `LicenseErrors.cs:70`; manual G15 d/g-TalkBack/h.
**Out-of-scope:** CustomerDetail phone Description (code-gaps).

### Teardown
Removed `.cursor/ralph/`.

### Status
**COMPLETE WITH NOTES** — unpushed G15 re-verified; gates PASS; no product changes.

## Ralph Balanced — Expenses Domain chunk 1 — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` (also ahead 1 docs commit `cc6be27`, out of this product slice)
- Slice: WT Domain expenses chunk 1 (`Expense`, `ExpenseCategory`, `MoneyRules`, Payment amount extract, `ExpenseTests`, plan mark Done)
- Build: `JAVA_HOME=…/jbr dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror`
- Lint: `-warnaserror` (no format gate)
- Test: Domain/App/Web/Mobile/Api `-c Release --no-build`; Postgres :5435; JBR
- Commit auth: yes (RALPH BALANCED prompt)

### Snapshot
```
cmd: git status --porcelain
out: M expenses-plan, FieldLimits, Payment, DomainRuleException; ?? MoneyRules, Expense, ExpenseCategory, ExpenseTests (+ .cursor/ralph/)
cmd: git diff --stat
out: 4 files changed, 9 insertions(+), 18 deletions(-) (+ untracked domain/tests)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | Read Expense/Payment/MoneyRules; Domain Expense+Payment tests | none P0–P1 | DetachOrder cut totals 2000 (`Expense.cs:140-144`); Void idempotent first-reason; Create requires todayAccra |
| 2 Edge | PASS | ExpenseTests amounts/dates/notes/order/user bounds | none | future SpentOn rejected; note 2001 → EXPENSE_INVALID; category case-sensitive |
| 3 Security | PASS | control-char strip in CleanText; no secrets | none | Domain-only; no IO/auth in slice |
| 4 Senior | PASS | class C; MoneyRules shared; SyncEntityType deferred (plan) | Fixed P2 plan `IsKnown`→`IsValid` | Payment.CheckAmount delegates; Domain 116 tests |
| 5 UI/UX | N/A | no UI files in slice | — | Domain + plan + tests only |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 368 / 226 / 391 / 357; Failed 0 |
| Perf | N/A | class C | no harness |

### Findings
**Fixed:** expenses-plan.md:53 `IsKnown` → `IsValid` (matches `ExpenseCategory.IsValid` / `PaymentMethod.IsValid`).
**Dismissed:** DetachOrder length math exact 2000; blank void reason allowed on Domain (Application min later per plan).
**Open:** domain skill still lists Expense as Planned (out of WT ownership this run).
**Out-of-scope:** unpushed docs commit `cc6be27`; EF/Api/UI chunks 2+.

### Teardown
Removed `.cursor/ralph/`.

### Status
**COMPLETE** — Domain chunk 1 gates PASS; no P0/P1.

## Ralph Balanced — expenses unpushed re-verify — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` ahead 2 of `origin/PlanNameCatalogue`
- Slice: `origin/PlanNameCatalogue..HEAD` = `54b0fbd` (Domain chunk 1) + `cc6be27` (expenses plan docs)
- WT clean except this run's `.cursor/ralph/` (+ skill fix)
- Gates: Release `-warnaserror`; full test projects; Postgres :5435; JBR
- Commit auth: yes (user: run the loop again on unpushed commits)

### Snapshot
```
cmd: git status --porcelain
out: ?? .cursor/ralph/ (then M domain SKILL after fix)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: 54b0fbd Add Expense domain model…
     cc6be27 Docs: add full Step 7b-2 expenses implementation plan…
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | Read Expense Create/Void/DetachOrder; ExpenseTests×30 | none | DetachOrder / Void / todayAccra unchanged |
| 2 Edge | PASS | ExpenseTests filter; SyncEntityType has no Expense | none | SyncEntityType deferred to chunk 5 per plan |
| 3 Security | PASS | Domain-only money rules; no new sinks in unpushed | none | — |
| 4 Senior | PASS | class C; plan↔code Create/IsValid/MoneyRules | Fixed P2: domain skill still **Planned** after Domain built (`needlebase-domain/SKILL.md:39`) | plan already marks chunk 1 Done |
| 5 UI/UX | N/A | no UI product files in unpushed range | — | plan UI sections are docs only |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 368 / 226 / 391 / 357; Failed 0 |
| Perf | N/A | class C | no harness |

### Findings
**Fixed:** `needlebase-domain/SKILL.md:39` and §Expense heading — status **Planned** → **Domain built** (chunk 1).
**Dismissed:** plan Create/IsValid/MoneyRules match code; SyncEntityType.Expense correctly absent until chunk 5.
**Open:** none in-slice.
**Out-of-scope:** EF/Api/UI chunks 2+.

### Teardown
Removed `.cursor/ralph/`.

### Status
**COMPLETE** — unpushed expenses slice re-verified; skill status aligned; gates PASS.

## Ralph Balanced — Expenses EF chunk 2 — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` (in sync with origin at start)
- Slice: WT expenses chunk 2 — EF `Expenses`, migration `20261127084705_Expenses`, audit, epoch restamp, delete-account purge, `ExpensePersistenceTests`
- Build/lint/test: Release `-warnaserror`; full test projects; Postgres :5435; JBR
- Commit auth: yes (RALPH BALANCED prompt)

### Snapshot
```
cmd: git status --porcelain
out: M plan/IApplicationDbContext/AuditActions/SyncEntityType/AuditCapture/DeleteAccount/ApplicationDbContext/snapshot;
     ?? Expenses migration (+Designer), ExpensePersistenceTests, .cursor/ralph/
cmd: git diff --stat
out: 8 files changed, 186 insertions(+), 4 deletions(-) (+ untracked migration/tests)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | Read migration/DbContext/DeleteAccount; ExpensePersistence×3 | Fixed P2: cross-tenant assert missing | Expenses purged before Orders; Order FK NoAction; trigger `trg_expenses_sync_tx` |
| 2 Edge | PASS | FK delete test; SyncEntityType.Expense ∉ All | none | sync ops still rejected until chunk 5 |
| 3 Security | PASS | tenancy filter + audit note mask; purge IgnoreQueryFilters by BusinessId | Fixed isolation test | notes not in ChangesJson (test evidence) |
| 4 Senior | PASS | class B (new table/indexes/epoch UPDATE); no harness → perf N/A | none | CI `db-migrate.sh script` still generates artifact; not a committed file |
| 5 UI/UX | N/A | no UI files in slice | — | EF/Infra/tests/plan only |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 368 / 226 / 391 / 360; Failed 0 |
| Perf | N/A | class B, no harness | — |

### Findings
**Fixed:** `ExpensePersistenceTests.cs:86` — asserted null-business fail-closed only while named “OtherShopsCantSeeIt”; now sets `CurrentBusinessId` to another shop and asserts hide + `IgnoreQueryFilters` still sees owner row.
**Dismissed:** SyncEntityType.Expense constant out of `All` until chunk 5 (plan + code comment).
**Open:** none in-slice.
**Out-of-scope:** ExpensesViewed / API service (chunk 3); sync push/pull (chunk 5).

### Teardown
Removed `.cursor/ralph/`.

### Status
**COMPLETE** — chunk 2 gates PASS; cross-tenant persistence test strengthened.

## Ralph Balanced — Expenses EF unpushed re-verify — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` ahead 1 of `origin/PlanNameCatalogue`
- Slice: `origin/PlanNameCatalogue..HEAD` = `63b0afe` (Expenses EF chunk 2)
- WT clean except this run's `.cursor/ralph/` (+ domain skill status fix)
- Gates: Release `-warnaserror`; full test projects; Postgres :5435; JBR
- Commit auth: yes (user: run the loop again on unpushed commits)

### Snapshot
```
cmd: git status --porcelain
out: ?? .cursor/ralph/ (then M domain SKILL after fix)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: 63b0afe Add Expenses EF table, sync stamps, audit capture, and purge (chunk 2).
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | Read DeleteAccount/migration/DbContext; ExpensePersistence×3 | none new | Expenses before Orders; FK NoAction; trigger present |
| 2 Edge | PASS | SyncEntityType.Expense ∉ All; FK delete log | none | sync ops still gated until chunk 5 |
| 3 Security | PASS | cross-tenant CurrentBusinessId=otherBusinessId asserts | none | prior fix at ExpensePersistenceTests.cs:103-110 holds |
| 4 Senior | PASS | class B; no harness → perf N/A | Fixed P2: domain skill still “EF remain” after chunk 2 (`needlebase-domain/SKILL.md:39`) | plan already marks chunk 2 Done |
| 5 UI/UX | N/A | no UI in unpushed product files | — | — |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 368 / 226 / 391 / 360; Failed 0 |
| Perf | N/A | class B, no harness | — |

### Findings
**Fixed:** `needlebase-domain/SKILL.md:39` (+ §Expense) — **Domain built; EF remain** → **Domain + EF built**; Api/sync/UI remain.
**Dismissed:** tenancy/FK/sync-stamp coverage from prior review re-checked via tests.
**Open:** none in-slice.
**Out-of-scope:** chunks 3+ (API/reports/sync/UI).

### Teardown
Removed `.cursor/ralph/`.

### Status
**COMPLETE** — unpushed EF chunk re-verified; skill status aligned; gates PASS.

## Ralph Balanced — Expenses API chunk 3 — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` (in sync with origin at start)
- Slice: WT expenses chunk 3 — `ExpenseService`/`ExpenseRules`/`ExpensesController`/`IPlanAccess`/`PlanAccess`, order-delete `ORDER_HAS_EXPENSES`, errors/ApiProblems, tests
- Build/lint/test: Release `-warnaserror`; full test projects; Postgres :5435; JBR
- Commit auth: yes (RALPH BALANCED prompt)

### Snapshot
```
cmd: git status --porcelain
out: 15 modified + ExpensesController, IPlanAccess, ExpenseDtos, ExpenseRules/Service/IExpenseService, PlanAccess, ExpensesApiTests, ExpenseRulesTests (+ .cursor/ralph/)
cmd: git diff --stat
out: 15 files changed, 96 insertions(+), 5 deletions(-) (+ untracked ~1.3k LOC)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | Read ExpenseService/Controller/OrderService; ExpensesApi×18 | Fixed P2: HasExpenses copy said “Void them” but voided still block delete | locks id→order; replay/conflict; plan-before-role |
| 2 Edge | PASS | ExpenseRulesTests; lenient clamp/map; filter invalid | none | orderId-only list skips month |
| 3 Security | PASS | OtherShops 404; Tailor 403; plan gating; read-only writes | none | notes not in audit; WriteGuard global |
| 4 Senior | PASS | class B (list aggregates); no harness → perf N/A | none | IPlanAccess extract; DataAnnotations deviation noted in plan |
| 5 UI/UX | PASS (mobile copy only) | SyncStatusViewModel DeleteRefused text + PaymentSyncTests | Fixed misleading ORDER_HAS_EXPENSES detail | Sync details “payments or expenses” |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 381 / 226 / 391 / 375; Failed 0 |
| Perf | N/A | class B, no harness | — |

### Findings
**Fixed:** `OrderErrorMessages.cs:35` — “Void them or keep the order” → “Keep the order.” (voided expenses still block delete, same as payments; proven by `OrderLink_MustBeThisShopsOrder_AndBlocksOrderDelete`). Plan + OrderService comment aligned.
**Dismissed:** PlanAccess on unfiltered Businesses; SubscriptionWriteGuard covers writes; SyncEntityType.Expense still out of All.
**Open:** domain skill still says Api remain (not in this WT).
**Out-of-scope:** reports/sync/UI chunks 4+.

### Teardown
Removed `.cursor/ralph/`.

### Status
**COMPLETE** — chunk 3 gates PASS; misleading delete copy fixed.

## Ralph Balanced — Expenses API unpushed re-verify — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` ahead 1 of `origin/PlanNameCatalogue`
- Slice: `origin/PlanNameCatalogue..HEAD` = `ed5e512` (Expenses API chunk 3)
- WT clean except this run's `.cursor/ralph/` (+ domain skill status fix)
- Gates: Release `-warnaserror`; full test projects; Postgres :5435; JBR
- Commit auth: yes (user: run the loop again on unpushed commits)

### Snapshot
```
cmd: git status --porcelain
out: ?? .cursor/ralph/ (then M domain SKILL after fix)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: ed5e512 Add expenses API service, plan gating, and ORDER_HAS_EXPENSES (chunk 3).
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | Read AccessAsync/OrderErrorMessages; ExpensesApi filter | none new | prior HasExpenses copy fix holds (`OrderErrorMessages.cs:35`) |
| 2 Edge | PASS | ExpenseRules 13; plan Theory cases | none | — |
| 3 Security | PASS | plan-before-role; ExpensesApi tenancy/role/plan | none | — |
| 4 Senior | PASS | class B; no harness → perf N/A | Fixed P2: domain skill still “Api remain” after chunk 3 (`needlebase-domain/SKILL.md:39`) | plan already marks chunk 3 Done; api skill already lists endpoints |
| 5 UI/UX | N/A | no new UI product files beyond prior SyncStatus copy | — | — |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 381 / 226 / 391 / 375; Failed 0 |
| Perf | N/A | class B, no harness | — |

### Findings
**Fixed:** `needlebase-domain/SKILL.md:39` (+ §Expense) — **Domain + EF built; Api remain** → **Domain + EF + Api built**; sync/UI remain.
**Dismissed:** ORDER_HAS_EXPENSES copy, DI wiring, AccessAsync order re-checked.
**Open:** none in-slice.
**Out-of-scope:** chunks 4+ (reports/sync/UI).

### Teardown
Removed `.cursor/ralph/`.

### Status
**COMPLETE** — unpushed API chunk re-verified; skill status aligned; gates PASS.

## Ralph Balanced — Expenses reports/export/email (chunk 4) — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` (tracking origin)
- Slice: working-tree expenses plan chunk 4 (reports calculator/DTO + MonthlyReportService + export expenses + MonthProfitSummary email + tests + expenses-plan Done)
- Commit auth: yes (this prompt: `commit: authorized`)
- Gates: `dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror`; lint = warnaserror; Domain/Application/Web/Mobile/Api tests; Postgres :5435; `JAVA_HOME=/opt/android-studio/jbr`
- Runtime: DB available (Api tests passed)

### Snapshot
```
cmd: git status --porcelain
out: M expenses-plan.md, ReportDtos, EmailTemplates, MonthlyReports, PlanEmailService,
     BusinessExportService, MonthlyReportService, ReportLocalService,
     ExpensesApiTests, ReportsApiTests, StaffApiTests.PlanEmails, MonthlyReportTests, PlanEmailTests
     ?? .cursor/ralph/
cmd: git diff --stat
out: 13 files changed, 507 insertions(+), 16 deletions(-)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | Read MonthlyReports.cs:107–187, MonthlyReportService.cs:80–88, PlanEmailService.cs:556–570; App MonthlyReport×; Api Expenses_give_spent + MonthlySummary_adds | none | voided filtered server+calculator; trend[^1]==chosen month (Range offset); LoadLastMonthProfit Accra window matches calculator |
| 2 Edge | PASS | IsEmpty+ExpenseCount (ReportDtos.cs:110); unknown category→Other; optional expenses=[]; ReportLocalService `[]` until chunk 6; MoneyRules amount floor | none | email note uses Spent>0 ≡ HasExpenses for valid amounts |
| 3 Security | PASS | ExportController RequireOwner + Export entitlement; expenses `BusinessId==`; email IgnoreQueryFilters + BusinessId (same as payments); voided out of profit | none | export includes voided by design (full record / downgrade) |
| 4 Senior | PASS | class B (extra expense query + export array); no report harness → perf N/A; additive DTO fields; CostsNote retained until UI 7/8 | none | CategoryOrder ToList/call P4 only |
| 5 UI/UX | N/A | no report UI markup in slice; CostsNote still Index.cshtml:60 / ReportsPage.cs:118 per plan | — | ProfitNote/CostsNote intentional until chunks 7–8 |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` BUILD_EXIT:0 |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api Release `--no-build` | 116 / 387 / 226 / 391 / 378; Failed 0; focused MonthlyReport|PlanEmailTemplate 46; Expenses_give_spent|Export_Includes|MonthlySummary_adds 3 |
| Perf | N/A | class B, no harness | — |

### Findings
**Fixed:** none (no P0/P1; no autonomy-eligible P2).
**Dismissed:**
- Email profit note shorter than `ReportRules.ProfitNote` (`EmailTemplates.cs:108` vs `MonthlyReports.cs:33–34`) — P3 copy; email omits “costs you haven't recorded” / “for the month”; intentional brevity; tests lock wording.
- `CostsNote` still on web/phone reports — deferred to chunks 7/8 (`MonthlyReports.cs:25–30`).
- `ReportLocalService` empty expenses — chunk 6 LocalExpense (`ReportLocalService.cs` comment).
- Export voided expenses — same as payments; `ExpensesApiTests.Export_IncludesLiveAndVoidedExpenses_EvenAfterDowngradeToStarter`.
**Open:** none blocking. Residual P3: unify email note with `ReportRules.ProfitNote` later if desired.
**Out-of-scope:** sync chunk 5, LocalExpense/UI chunks 6–8.

### Teardown
Removed `.cursor/ralph/` (scratchpad + dir).

### Status
**COMPLETE** — chunk 4 gates PASS; no open P0/P1; commit authorized.

## Ralph Balanced — Expenses chunk 4 unpushed re-verify — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` ahead 1 of `origin/PlanNameCatalogue`
- Slice: `origin/PlanNameCatalogue..HEAD` = `8e2139f` (expenses reports/export/email chunk 4)
- WT clean at start (then skill status fixes + this log)
- Gates: Release `-warnaserror`; Domain/App/Web/Mobile/Api tests; Postgres :5435; JBR
- Commit auth: yes (user: run the loop again on unpushed commits)

### Snapshot
```
cmd: git status --porcelain
out: (clean at discovery)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: 8e2139f Add expense spend and profit to monthly reports, export, and owner email (chunk 4).
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | MonthlyReports.cs:126–186; MonthlyReportService.cs:80–88; PlanEmailService.cs:556–570 | none new | voided + Accra windows hold |
| 2 Edge | PASS | ReportLocalService `[]`; IsEmpty+ExpenseCount; prior focused tests | none | — |
| 3 Security | PASS | Export RequireOwner + BusinessId; email IgnoreQueryFilters+BusinessId | none | voided in export by design |
| 4 Senior | PASS | class B; no harness → perf N/A | Fixed P2: skills still said expenses “planned/not started” after chunks 1–4 (`needlebase-plans/SKILL.md`, `needlebase-api/SKILL.md:111`, `needlebase-phases/SKILL.md:34`, `needlebase-domain/SKILL.md:39`) | CostsNote until UI 7/8; email note P3 residual |
| 5 UI/UX | N/A | no UI in unpushed product files | — | CostsNote still Index.cshtml:60 / ReportsPage.cs:118 |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` BUILD_EXIT:0 |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 387 / 226 / 391 / 378; Failed 0 |
| Perf | N/A | class B, no harness | — |

### Findings
**Fixed:** skill status drift — plans/api/phases/domain now say expenses chunks 1–4 built; sync/UI remain.
**Dismissed:** product findings from prior chunk-4 review (email note P3; CostsNote deferred).
**Open:** none in-slice.
**Out-of-scope:** sync/UI chunks 5–8.

### Teardown
Removed `.cursor/ralph/`. Backups under `~/.cache/needlebase-ci/backups/ralph-unpushed-c4-20261127-102038/`.

### Status
**COMPLETE** — unpushed chunk 4 re-verified; skill status aligned; gates PASS.

## Ralph Balanced — Expenses chunk 4 unpushed re-verify (2) — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` ahead 2 of `origin/PlanNameCatalogue`
- Slice: `origin/PlanNameCatalogue..HEAD` = `0a510c5` + `8e2139f` (chunk 4 product + prior skill re-verify)
- WT clean at start
- Gates: Release `-warnaserror`; Domain/App/Web/Mobile/Api; Postgres :5435; JBR
- Commit auth: yes (user: rerun the loop again)

### Snapshot
```
cmd: git status --porcelain
out: (clean)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: 0a510c5 Record expenses chunk 4 unpushed re-verify; mark Domain–reports built in skills.
     8e2139f Add expense spend and profit to monthly reports, export, and owner email (chunk 4).
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | MonthlyReports void filter; MonthlyReportService SpentOn window; PlanEmail LoadLastMonthProfit; OrderErrorMessages.cs:35 Keep the order | none | prior fixes hold |
| 2 Edge | PASS | focused MonthlyReport|PlanEmail 46; Api spent/export/email 3 | none | ReportLocalService `[]` until chunk 6 |
| 3 Security | PASS | export BusinessId filters; RequireOwner | none | — |
| 4 Senior | PASS | class B; no harness → perf N/A; skill status re-check | none | web/mobile/sync “planned” = UI/sync surfaces still open (correct); plans/api/phases/domain already say chunks 1–4 done |
| 5 UI/UX | N/A | CostsNote still Index.cshtml:60 / ReportsPage.cs:118 | — | deferred chunks 7–8 |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` BUILD_EXIT:0 |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 387 / 226 / 391 / 378; Failed 0 |
| Perf | N/A | class B, no harness | — |

### Findings
**Fixed:** none.
**Dismissed:** web/mobile/sync skill “Expenses planned” — scoped to UI/sync chunks still open; not false “not started” for Domain–reports.
**Open:** none.
**Out-of-scope:** chunks 5–8; email↔ProfitNote P3 copy.

### Teardown
Removed `.cursor/ralph/`.

### Status
**COMPLETE** — clean re-verify; gates PASS; no product or skill changes required beyond this log.

## Ralph Balanced — Expenses server sync (chunk 5) — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` (synced with origin at start; dirty WT)
- Slice: working-tree expenses plan chunk 5 (SyncEntityType.Expense in All, SyncExpenseDto/cursor phase 5 `z`, SyncService ApplyExpenseAsync + pull, ExpenseSyncTests×11, expenses-plan Done)
- Commit auth: yes (this prompt)
- Gates: Release `-warnaserror`; Domain/App/Web/Mobile/Api; Postgres :5435; JBR

### Snapshot
```
cmd: git status --porcelain
out: M expenses-plan, SyncDtos, SyncCursor, SyncMapper, SyncService, SyncEntityType
     ?? ExpenseSyncTests.cs
     (+ skill status updates this run)
cmd: git diff --stat (product)
out: 6 files changed, 176 insertions(+), 17 deletions(-) (+ 511-line test file)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | Read ApplyExpenseAsync/ApplyPaymentAsync parity; ExpenseService same/detached; phase advance PhaseLast=5; ExpenseSync×11 | none | void merge first-reason; delete refused; detached order replay |
| 2 Edge | PASS | lenient Fuel→Other; amount 0 fail; old cursor without `z`; Starter reset; Tailor skip→Manager pull | none | read-only push via SyncController AllowWhenReadOnly |
| 3 Security | PASS | plan→EXPENSES_NOT_ALLOWED; Tailor→FORBIDDEN; other shop id→REJECTED (no leak); pull gated role+plan | none | recorder ids not in SyncExpenseDto |
| 4 Senior | PASS | class B (extra pull phase); no harness → perf N/A; additive pull Expenses null-ok | Fixed P2: skills still “sync remain/planned” after chunk 5 | ExpenseMismatch “keep both” copy P3 |
| 5 UI/UX | N/A | no UI files in slice | — | — |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` BUILD_EXIT:0 |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 387 / 226 / 391 / 389; ExpenseSync 11; Failed 0 |
| Perf | N/A | class B, no harness | — |

### Findings
**Fixed:** skill status → chunks 1–5 (server sync) done; Mobile.Data/UI remain (`plans`/`api`/`phases`/`domain`/`mobile-sync` SKILL.md).
**Dismissed:** product logic; cursor key `z` vs plan’s `x` (documented deviation in expenses-plan).
**Open:** none blocking. Residual P3: ExpenseMismatch conflict wording.
**Out-of-scope:** Mobile.Data chunk 6, UI 7–8.

### Teardown
Removed `.cursor/ralph/`. Backups `~/.cache/needlebase-ci/backups/ralph-expense-sync-*`.

### Status
**COMPLETE** — chunk 5 gates PASS; no open P0/P1; commit authorized.

## Ralph Balanced — Expenses sync chunk 5 unpushed re-verify — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` ahead 1 of `origin/PlanNameCatalogue`
- Slice: `origin/PlanNameCatalogue..HEAD` = `e1d4cc4` (expense sync push/pull phase 5)
- WT clean at start
- Gates: Release `-warnaserror`; Domain/App/Web/Mobile/Api; Postgres :5435; JBR
- Commit auth: yes (user: run the loop again on unpushed commits)

### Snapshot
```
cmd: git status --porcelain
out: (clean)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: e1d4cc4 Add expense sync push/pull phase 5 and mark chunk 5 built in skills.
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | ApplyExpenseAsync; PhaseExpenses; ExpenseSync×11; OrderErrorMessages Keep the order | none | prior chunk-5 review holds |
| 2 Edge | PASS | focused ExpenseSync 11 (old cursor, Starter reset, Tailor, lenient, read-only) | none | — |
| 3 Security | PASS | plan/role fail codes; Rejected on foreign id; pull expensesAllowed | none | — |
| 4 Senior | PASS | class B; no harness → perf N/A; skill status re-check | none | plans/api/phases/domain/mobile-sync already say chunks 1–5; web/mobile UI “planned” = chunks 7–8 still open |
| 5 UI/UX | N/A | no UI in unpushed product files | — | — |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` BUILD_EXIT:0 |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 387 / 226 / 391 / 389; ExpenseSync 11; Failed 0 |
| Perf | N/A | class B, no harness | — |

### Findings
**Fixed:** none.
**Dismissed:** skill status already aligned in `e1d4cc4`; web/mobile “planned step 7b-2” correct for UI.
**Open:** none. Residual P3: ExpenseMismatch “keep both” copy (`SyncService.cs:732`).
**Out-of-scope:** Mobile.Data chunk 6, UI 7–8.

### Teardown
Removed `.cursor/ralph/`.

### Status
**COMPLETE** — clean re-verify; gates PASS; no product/skill changes beyond this log.

## Ralph Balanced — Expenses Mobile.Data (chunk 6) — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` (synced with origin at start; dirty WT)
- Slice: working-tree expenses plan chunk 6 (LocalDbContext Expenses v13, SyncQueueWriter, SyncEngine plan-hold/apply/prune/mismatch, ExpenseLocalService, ReportLocalService spent, FreeUpgrade remap, MauiProgram DI+Tailor purge, ExpensesMobileTests×12, expenses-plan Done)
- Commit auth: yes (this prompt)
- Gates: Release `-warnaserror`; Domain/App/Web/Mobile/Api; Postgres :5435; JBR

### Snapshot
```
cmd: git status --porcelain
out: M expenses-plan, LocalDbContext, LocalSchemaMigrator, SyncQueueWriter, FreeUpgrade,
     ReportLocalService, SyncEngine, MauiProgram, PaymentSyncTests
     ?? ExpenseLocalService.cs, IExpenseLocalService.cs, ExpensesMobileTests.cs
cmd: git diff --stat (product before skills)
out: 9 files changed, 274 insertions(+), 20 deletions(-) (+ ~925 LOC new)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | RunAsync push→pull→optional expense push; ApplyExpenseCopies; plan-hold; ExpensesMobile×12 | none | no FK to Orders; void merge; upgrade second push |
| 2 Edge | PASS | v12→v13 migrate; unknown plan allows record+hold; Starter refuses record; purge keeps queued; Free remap; wipe | none | EXPENSES_FORBIDDEN → Failed (plan: engine role-blind) |
| 3 Security | PASS | BusinessId filters; PlanSaysNo on record; read-only throws; Tailor purge after sync | none | — |
| 4 Senior | PASS | class B (extra entity type on push/pull); no harness → perf N/A | Fixed P2: skills still “Mobile.Data remain” | Sync details “Waiting for Workshop” deferred chunk 7 |
| 5 UI/UX | N/A | no expense screens in slice (MauiProgram DI only) | — | — |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` BUILD_EXIT:0 |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 387 / 226 / **403** / 389; ExpensesMobile 12; Failed 0 |
| Perf | N/A | class B, no harness | — |

### Findings
**Fixed:** skill status → chunks 1–6 done; phone/web UI remain (plans/api/phases/domain/mobile-sync/mobile).
**Dismissed:** product logic; EXPENSES_FORBIDDEN→Failed per plan; ExpenseMismatch copy P3.
**Open:** none blocking. Residual: Sync details held-item copy (chunk 7).
**Out-of-scope:** phone/web UI chunks 7–8.

### Teardown
Removed `.cursor/ralph/`. Backups `~/.cache/needlebase-ci/backups/ralph-expense-m6-*`.

### Status
**COMPLETE** — chunk 6 gates PASS; no open P0/P1; commit authorized.

## Ralph Balanced — Expenses Mobile.Data chunk 6 unpushed re-verify — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` ahead 1 of `origin/PlanNameCatalogue`
- Slice: `origin/PlanNameCatalogue..HEAD` = `0da8cbf` (local Expense SQLite sync chunk 6)
- WT clean at start
- Gates: Release `-warnaserror`; Domain/App/Web/Mobile/Api; Postgres :5435; JBR
- Commit auth: yes (user: run the loop again on unpushed commits)

### Snapshot
```
cmd: git status --porcelain
out: (clean)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: 0da8cbf Add local Expense SQLite sync (v13) with plan-hold queue and local service (chunk 6).
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | RunAsync.cs:51–82 plan-hold second push; ExpensesMobile×12 | none | prior chunk-6 review holds |
| 2 Edge | PASS | focused ExpensesMobile 12 (upgrade, migrate, purge, remap) | none | — |
| 3 Security | PASS | PlanSaysNo; PurgeHidden AfterRun; BusinessId scope | none | — |
| 4 Senior | PASS | class B; no harness → perf N/A; skill status re-check | none | plans/api/phases/domain/mobile-sync/mobile already say chunks 1–6; web “planned” = UI chunk 8 |
| 5 UI/UX | N/A | no expense screens in unpushed product files | — | — |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` BUILD_EXIT:0 |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 387 / 226 / 403 / 389; ExpensesMobile 12; Failed 0 |
| Perf | N/A | class B, no harness | — |

### Findings
**Fixed:** none.
**Dismissed:** skill status already aligned in `0da8cbf`; web UI planned correct.
**Open:** none. Residual: Sync details held-item copy (chunk 7); ExpenseMismatch P3 copy.
**Out-of-scope:** phone/web UI chunks 7–8.

### Teardown
Removed `.cursor/ralph/`.

### Status
**COMPLETE** — clean re-verify; gates PASS; no product/skill changes beyond this log.

## Ralph Balanced — Expenses phone UI (chunk 7) — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` (synced with origin at start; dirty WT)
- Slice: working-tree expenses plan chunk 7 (More→Expenses pages/VMs, order section, Reports Spent/Profit, Sync details held text, ExpensesUiTests×12)
- Commit auth: yes (this prompt)
- Gates: Release `-warnaserror`; Domain/App/Web/Mobile/Api; Postgres :5435; JBR

### Snapshot
```
cmd: git status --porcelain
out: M expenses-plan, AppShell, MauiProgram, OrderDetail/Reports/SyncStatus VMs, MorePage,
     OrderDetailPage.xaml, ReportsPage
     ?? Expense* VMs/pages, ExpenseUi, RecordExpense*, ExpensesUiTests
cmd: git diff --stat (product before skills)
out: 9 files changed, 302 insertions(+), 38 deletions(-) (+ ~2.1k LOC new)
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | ExpenseAccess.Gate; RecordExpense Validate/Save ULID; Reports AppearAsync | Fixed P2: `HasMoreExpenses` used page size (`OrderDetailViewModel.cs:128`) | IBackAware Cancel; plan-hold SyncItemRow |
| 2 Edge | PASS | ExpensesUi×12 (gate, month clamp, void, offline double-save, held text) | none | unknown plan = NotAllowed |
| 3 Security | PASS | Tailor/Free/Starter gated; Owner/Manager Money section; read-only record blocked | none | — |
| 4 Senior | PASS | class C (UI/VM); skills status update | Fixed P2 skills “phone UI remain” | uiux docs deferred chunk 9 |
| 5 UI/UX | PASS | Ui.Chrome NavBar hidden; AutomationIds; ProfitNote vs CostsNote; category bars text+amount; SemanticProperties | none | Record page Cancel not chrome Back (IBackAware) |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` BUILD_EXIT:0 |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 387 / 226 / **415** / 389; ExpensesUi 12; Failed 0 |
| Perf | N/A | class C | — |

### Findings
**Fixed:** `OrderDetailViewModel.cs:128` HasMoreExpenses → `list.Count > ExpensePreviewCount`. Skills → chunks 1–7 phone UI done; web remain.
**Dismissed:** See all uses HasExpenseRows (by design); ExpenseMismatch copy P3.
**Open:** none blocking.
**Out-of-scope:** web chunk 8; uiux docs chunk 9.

### Teardown
Removed `.cursor/ralph/`. Backups `~/.cache/needlebase-ci/backups/ralph-expense-ui7-*`.

### Status
**COMPLETE** — chunk 7 gates PASS; no open P0/P1; commit authorized.

## Ralph Balanced — Expenses phone UI chunk 7 unpushed re-verify — iteration 1 — 27 Nov 2026

### Discovery
- Repo: `/home/helmut/Documents/My Projects/NeedleBase`
- Branch: `PlanNameCatalogue` ahead 1 of `origin/PlanNameCatalogue`
- Slice: `origin/PlanNameCatalogue..HEAD` = `ffe2326` (phone Expenses screens chunk 7)
- WT clean at start
- Gates: Release `-warnaserror`; Domain/App/Web/Mobile/Api; Postgres :5435; JBR
- Commit auth: yes (user: run the loop again on unpushed commits)

### Snapshot
```
cmd: git status --porcelain
out: (clean)
cmd: git log --oneline origin/PlanNameCatalogue..HEAD
out: ffe2326 Add phone Expenses screens, order section, and reports profit UI (chunk 7).
```

### Pass ledger
| pass | state | tools | findings | dismissals |
|------|-------|-------|----------|------------|
| 1 Bugs | PASS | HasMoreExpenses `list.Count >` (OrderDetailViewModel.cs:129); ProfitNote on ReportsPage; ExpensesUi×12 | none | prior chunk-7 fix holds |
| 2 Edge | PASS | focused ExpensesUi 12 | none | — |
| 3 Security | PASS | ExpenseAccess.Gate Free/Tailor/Starter | none | — |
| 4 Senior | PASS | class C; skill status re-check | none | skills already chunks 1–7; web planned = chunk 8 |
| 5 UI/UX | PASS | ProfitNote vs CostsNote; held Sync text; AutomationIds | none | — |

### Gate state table
| gate | state | command | tail |
|------|-------|---------|------|
| Build | PASS | Release `--no-incremental -warnaserror` | `0 Warning(s) 0 Error(s)` BUILD_EXIT:0 |
| Lint | PASS | same `-warnaserror` | `0 Warning(s)` |
| Test | PASS | Domain/App/Web/Mobile/Api | 116 / 387 / 226 / 415 / 389; ExpensesUi 12; Failed 0 |
| Perf | N/A | class C | — |

### Findings
**Fixed:** none.
**Dismissed:** skill status already aligned in `ffe2326`; web UI planned correct.
**Open:** none.
**Out-of-scope:** web chunk 8; uiux docs chunk 9.

### Teardown
Removed `.cursor/ralph/`.

### Status
**COMPLETE** — clean re-verify; gates PASS; no product/skill changes beyond this log.
