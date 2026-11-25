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
