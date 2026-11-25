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

### Residual notes for humans
- Step 2 must call `PlanCatalog.Resolve` (not `ForCode` alone) and combine with `SubscriptionRules` / read-only / `IsClosed`.
- Full `NeedleBase.sln` Release still fails Android **XA0033** (OpenJDK `17.0.21.0.3` version parse) — pre-existing, unrelated.
- Do not commit `.cursor/file-backups/` or `.cursor/ralph/scratchpad.md`.
