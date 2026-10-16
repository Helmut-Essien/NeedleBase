# Slice 2 review log

## Iteration 1

### Findings fixed
| Severity | Source | Fix |
|----------|--------|-----|
| High | Bugbot: splash without business_id | Splash requires `GetBusinessIdAsync()` before main |
| High | Senior: EnsureCreated failure swallowed | DB init failure stops navigation |
| High | Senior: idempotent create race/cross-tenant | `IgnoreQueryFilters` + 409 Conflict |
| Medium | Security: PII after logout | `ILocalDatabaseService.WipeAsync` before logout |
| Medium | Bugbot: stale customer detail | `OnAppearing` reloads detail |
| Medium | Bugbot: edit resets MeasuredAtUtc | Preserve original date on edit |
| Medium | Bugbot: sort by Id | Client-side sort by `MeasuredAtUtc` |
| Medium | Senior: empty list vs missing customer | Measurements list → 404 |
| Medium | Senior: cascade delete load | Rely on FK cascade |
| Medium | Senior: no idempotent test | Added API + local tests |
| Medium | Senior: unbounded measurement list | Cap after sort |
| Medium | Senior: load without try/catch | Error messages on load failure |
| High | UI/UX suite | CanSubmit, SemanticProperties, placeholders, BindableLayout, unit UX, empty CTAs, sync-later copy |

### Findings dismissed
| Finding | Reason |
|---------|--------|
| AppShell tab icons all `dotnet_bot.png` | No distinct icon assets yet (product/design) |
| EnsureCreated never upgrades schema | Slice 4 migrations |
| API create-by-id ignores body vs mobile upsert | Intentional sync semantics |
| SQLCipher at rest | MVP residual; logout wipe addresses handoff |

---

## Iteration 2

### Findings fixed
| Severity | Source | Fix |
|----------|--------|-----|
| Medium | Bugbot/Senior: Take before OrderBy | Order then Take(100) |
| High | Senior: flaky idempotent test | `IdGenerator.NewId()` per run |
| Medium | UI/UX: sticky HasError | NotifyPropertyChangedFor |
| Medium | UI/UX: EmptyView CTA while busy | `ShowEmptyAdd` / `ShowEmptyMeasurements` |
| Low | CanSubmit hide warning | Removed Login/Register duplicates |

### Findings dismissed
| Finding | Reason |
|---------|--------|
| Wipe on login/register | Residual defense-in-depth; logout wipe + BusinessId filter sufficient |
| Dedicated test DB | Unique ULIDs fix conflict on shared DB |

---

## Iteration 3–4

### Findings fixed
| Severity | Source | Fix |
|----------|--------|-----|
| High | Bugbot: singleton VM PII after logout | `ClearSessionState` on logout |
| High | Bugbot: in-flight LoadAsync after logout | `_loadGeneration` guards + businessId re-check |
| Medium | UI/UX: garment key vs display name | `GarmentTemplateDisplay` |
| Medium | UI/UX: fields editable during save | Snapshot then `SetFieldsEnabled(false)` |
| Medium | Senior: stuck RefreshView spinner | Clear `IsRefreshing` when `IsBusy` |

### Final suite status (iteration 4+)
| Review | Status |
|--------|--------|
| Bugbot | CLEAN (no actionable High/Medium; validator discarded residual noise) |
| Security | CLEAN (medium+) |
| Senior Developer | CLEAN |
| UI/UX | CLEAN |

### Residual notes (human awareness)
- Tab icons still placeholder (`dotnet_bot.png`) until assets arrive — recipe now in `.cursor/skills/needlebase-mobile/tab-icons.md` (AssetTag SVG MauiImage pattern).
- Mobile DB still `EnsureCreated` until Slice 4 migrations.
- Login/register without SQLite wipe: in-app reads scoped by BusinessId; explicit logout wipes. Optional later: wipe-on-tenant-change at login.
- Prefer dedicated test DB in CI for isolation (process note).

---

## Iteration 5 (MauiIcons + Shell Tab restructure)

### Findings fixed
| Severity | Source | Fix |
|----------|--------|-----|
| High | Bugbot: ShowLoginAsync leaves tab stacks | `NavigationService` dismisses modals + pops each `//main/*` tab to root before login |
| High | Bugbot: detail page stale PII | `CustomerDetailViewModel.ClearCustomerFields` before load / on error |
| High | Bugbot: edit init race | Init from `ApplyQueryAttributes`; OnAppearing only `InitializeIfPendingAsync` after Yield |
| Medium | UI/UX: placeholder tabs no icons | MauiIcon on Orders/Payments/More placeholders |
| Low | UI/UX: Add affordance / chevron a11y | PersonAdd on Add button; decorative chevron description cleared |

### Findings dismissed
| Finding | Reason |
|---------|--------|
| Security medium+ | Clean — MauiIcons font package only; tenancy unchanged |
| Tab PNG placeholders | Superseded — MauiIcons.Material implemented |

### Remaining open
- Re-run Bugbot / Security / Senior / UI/UX on updated slice (iteration 6).

---

## Iteration 6

### Findings fixed
| Severity | Source | Fix |
|----------|--------|-----|
| Medium | Security: singleton PII on session-expiry login | `NavigationService.ShowLoginAsync` always calls `CustomersViewModel.ClearSessionState` |
| High | Bugbot: save during measurement load wipes fields | Edit init sets `IsBusy` + `_readyToSave`; Save gated until load completes |
| Medium | Senior: edit customer save during load | `CustomerEditViewModel` sets `IsBusy` while fetching |

### Findings dismissed
| Finding | Reason |
|---------|--------|
| Bugbot iter 6 (main pass) | CLEAN — prior Highs resolved |

### Remaining open
- Re-run full suite (iteration 7) for clean confirmation.

---

## Iteration 7–8

### Findings fixed
| Severity | Source | Fix |
|----------|--------|-----|
| High | Bugbot: Save enabled after failed measurement load | `ReadyToSave` + `CanSave`; form stays locked; Save bound to `CanSave` |
| Medium | Senior: IsBusy → derived CanSave/ShowEmpty* | `BaseViewModel.OnBusyChanged` virtual hook |

### Findings dismissed
| Finding | Reason |
|---------|--------|
| CustomerDetail lacks `_loadGeneration` | Transient page; stacks popped on login; `ClearCustomerFields` + null `businessId` abort; validator discarded |
| SQLCipher / login without wipe | Documented MVP residuals; logout wipe + BusinessId scoping |

### Final suite status
| Review | Status |
|--------|--------|
| Bugbot | CLEAN (incl. MeasurementEdit CanSave / NavigationService) |
| Security | CLEAN (medium+) |
| Senior Developer | CLEAN |
| UI/UX | CLEAN (MauiIcons tabs + empty states; CanSave UX) |

### Residual notes (human awareness)
- Mobile DB still `EnsureCreated` until Slice 4 migrations.
- Unencrypted SQLite at rest — logout wipe mitigates shared-device handoff.
- Login/register without SQLite wipe: reads scoped by BusinessId; `ShowLoginAsync` clears singleton VM PII.
- Prefer dedicated test DB in CI for isolation.
