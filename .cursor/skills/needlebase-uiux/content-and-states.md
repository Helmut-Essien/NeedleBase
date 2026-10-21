# Content, formatting, feedback & states (whole product)

Cross-cutting rules for every screen on mobile and web. Users are mostly **phone-first Ghanaian tailors**. Assume low-end Android, patchy data, English that may be a second language, and busy hands. Tags used below: **[code]** means true in the repo today; **[rec]** means a recommendation; **[planned]** means the feature isn't built yet.

---

## 1. Formatting

| Thing | Rule | Status |
|-------|------|--------|
| Culture | `en-GH` default culture in Api + Web `Program.cs`; `<html lang="en-GH">` in both layouts | [code] |
| Money | `GH₵ 1,250.00`: symbol, thousands separator, 2 decimals, tabular figures, right-aligned in tables. Format once in a shared helper; never string-concat "GHS" + amount ad hoc | [rec] |
| Partial pay | "GH₵200 of GH₵800 paid", balance "GH₵600 left": say what's **left**, not just totals | [rec] |
| Dates | `dd MMM yyyy` (e.g. `20 Oct 2026`): mobile measurement history uses `{0:dd MMM yyyy}` | [code] |
| Time zone | Store UTC; display in `Africa/Accra`. Accra is UTC+0, so bugs hide. Still call `.ToLocalTime()` before formatting `*Utc` fields (CustomerDetailPage currently formats `MeasuredAtUtc` directly) | [rec] |
| Relative dates | Home/lists: "Due today", "Due tomorrow", "2 days late". Absolute date on detail screens | [rec] |
| Phone | Accept `024…`, `24…`, `+233 24…`; store E.164 `+233245550123`; display `+233 24 555 0123`. Placeholders in code: Register `+233...`, CustomerEdit `+233 24 555 0123` | partly [code] |
| Measurements | Value + unit always together (`92 cm`). Unit is per profile (`MeasurementUnit` = `cm` / `in`); edit page shows `Unit: {0}` and a suffix next to each field | [code] |
| Names | `GarmentTemplate.DisplayName()` for template labels (e.g. "Slit & kaba"). Never show raw keys like `slit_and_kaba` | [code] |

## 2. Voice & microcopy

- **No entry points to features that don't exist.** No Notifications bell until a real notifications screen ships (the Home bell was removed). No Forgot password link until reset is built (hidden on mobile and web) [code].

- Short, plain, friendly English. Aim for a reading age of about 12. One idea per sentence. Avoid jargon (sync **queue**, **entity**, **tenant**, **401**).
- Buttons are verbs: "Save customer", "Record payment", "Try again". Avoid "OK"/"Submit" except in confirmations.
- Use the same words everywhere: **Customer** (not client), **Order** (not job), **Measurements**, **Payment**, **Balance**, **Ready to collect**, **Collected**. Status names come only from the domain list in §6.
- Errors say what to do next ("Check your connection and try again").
- Local save is always "Saved on this device" (see [offline-sync.md](offline-sync.md)). Mobile Home uses "Saved on this device · syncs when online" and "N customers saved on this device" [code]. Never "on this phone".
- **Icons + labels together** for low literacy: never icon-only for primary actions or tabs. Home quick actions have text labels (`Add customer`, `New order`, `Payment`, `Measure`) [code]; keep an icon above each.

## 3. Feedback: which surface

| Situation | Use | Example |
|-----------|-----|---------|
| Field problem | Inline under field ([validation.md](validation.md)) | "Enter a phone number" |
| Action succeeded, nothing more to do | Toast (CommunityToolkit `Toast`, short) | "Saved on this device" |
| Page/load failed | Inline banner/state with Retry, in place of content | "Couldn't load customers. Try again" |
| Irreversible or money-affecting | Confirm dialog (`DisplayAlert` / Bootstrap modal) naming the thing + consequence | "Delete Efua Mensah's measurements? This can't be undone." Buttons: **Delete** / **Cancel** |
| Needs a choice to continue | Dialog or bottom sheet | Conflict resolution |
| Persistent condition | Banner/chip, not toast | Offline, plan expired |

Never use a toast for errors the user must act on. Never stack dialogs. A destructive button is red text or fill (`SemanticError`), and the safe option is the default.

## 4. States per screen type

| Screen type | Loading | Empty | Error | Offline |
|-------------|---------|-------|-------|---------|
| List (Customers, Orders, Payments) | Skeleton rows [rec]; today `ActivityIndicator` [code] | `CollectionView.EmptyView` icon + 1 line + 1 CTA ("Add your first customer") [code on Customers/CustomerDetail] | Inline "Couldn't load" + Try again; keep showing cached rows if any | Local list still works; unsynced work shows in the sync chip + Sync details (pending / needs-attention counts) [code: `SyncStatusViewModel`, `SyncDetailsPage`]. Per-row pending badge not built yet [planned Slice 8 polish, `needlebase-phases/phases.md`] |
| Detail | Skeleton header [rec] | n/a | "This record isn't available" + Back | Read local copy |
| Form | Disable primary button + spinner (`IsBusy`) [code] | n/a | Inline field + page error | Save locally, toast "Saved on this device" |
| Dashboard/Home | Skeleton cards | Honest zeros + "Nothing needs attention right now" [code] | Card-level error; other cards still render | Works from local data |
| Auth | Button busy state [code] | n/a | Page-level message (ProblemDetails `detail`, or the client's generic copy) | "You're offline. Connect to sign in." (login needs network; existing session keeps working offline) |
| Placeholder (unbuilt tab) | n/a | Payments/More placeholders: icon + h1 + "arrives in a later slice" [code] | n/a | n/a |

Skeletons: grey `Gray200` blocks matching the final layout. No shimmer animation on low-end devices [rec].

## 5. API errors to user copy

Api returns **RFC 9457 ProblemDetails** (`application/problem+json`) for every error [code: `NeedleBase.Api/Http/ApiProblems.cs`, `AddProblemDetails` + `UseStatusCodePages` + `UseExceptionHandler` in Program.cs]. Fields: `title` (short, user-safe), `detail` (one user-safe sentence), `status`, `code` (stable machine id, e.g. `INVALID_CREDENTIALS`, `CUSTOMER_NOT_FOUND`, `MEASUREMENT_NOT_FOUND`, `CUSTOMER_CONFLICT`, `MEASUREMENT_CONFLICT`, `CUSTOMER_HAS_ORDERS`, `CUSTOMER_SAVE_FAILED`, `MEASUREMENT_SAVE_FAILED`, `ORDER_LOCKED`, `ORDER_STATUS_INVALID`, `ASSIGNEE_INVALID`, `ORDER_TOTAL_MISMATCH`, `SESSION_INVALID`, `VALIDATION_FAILED`, `RATE_LIMITED`, `LOGIN_THROTTLED`, `PHONE_INVALID`, `STALE_UPDATE`, `SYNC_MAINTENANCE`; device-only `STORAGE_FULL`), `errors` (validation only), `traceId`.
Client rules:
- Switch on `code` for behaviour. Show `detail` (or `title`) as copy; for `errors`, map each key to its field's inline error.
- 409 conflicts appear inline on the relevant field when possible.
- Network/timeout: "Couldn't reach NeedleBase. Check your connection." Never show exception text (`ex.Message`).
- 5xx outside Development is always "Something went wrong on our side. Please try again."
[code] Auth copy per `code` lives in one place: `NeedleBase.Application/Contracts/Errors/AuthErrorMessages.cs` (used by mobile `AuthService.LastErrorMessage` and web `ApiAuthService.LastErrorMessage`). Registration failures never reveal whether an email exists (`REGISTER_FAILED` is generic by design). Order copy per `code`: `OrderErrorMessages.cs` (API `detail` and mobile local errors use the same text).

## 6. Status names & colours (single source)

From domain: `New → Cutting → Sewing → Fitting → Ready → Collected`, plus `Cancelled`. **Overdue** is derived, a badge next to the status. [code: `Mobile/Helpers/OrderStatusPresentation.cs`, Home stages use `Accent(status)`]. Orders saved offline say **"Saved on this device"** (then sync in the background, Slice 4). Unknown statuses from a newer server show **Other status** (`OrderStatusPresentation.UnknownStatusLabel`). UI label "Ready" is shown as **Ready to collect** (matches Home "READY TO COLLECT" [code]).

| Status | Colour token | Icon (Material) [rec] |
|--------|--------------|------------------------|
| New | `Gray600` text on `Gray200` | `FiberNew` |
| Cutting | `SemanticInfo` tint | `ContentCut` |
| Sewing | `PrimaryDark` on `PrimaryLight` | `Checkroom` |
| Fitting | `SemanticWarning` tint | `Straighten` |
| Ready to collect | white on `PrimaryDark` | `CheckCircle` |
| Collected | `Gray600` muted | `Done` |
| Cancelled | `SemanticError` muted | `Cancel` |
| Overdue (badge) | `SemanticOverdue` on `SemanticErrorLight` (distinct from Fitting) | `Schedule` |

Chip = icon + text always. Check icon names exist in `MauiIcons.Material.MaterialIcons` before use.

## 7. Forms & input

| Field | Mobile | Web |
|-------|--------|-----|
| Email | `Keyboard="Email"` [code] | `type="email" autocomplete="email"` |
| Phone | `Keyboard="Telephone"` [code Register, CustomerEdit] | `autocomplete="tel"` [code Register] + `type="tel"` [rec] |
| Measurement | `Keyboard="Numeric"` + unit suffix [code] | `inputmode="decimal"` |
| Money | `Keyboard="Numeric"`, GH₵ prefix [planned] | `inputmode="decimal"` |
| Name | `Keyboard="Text"`, capitalise words | `autocomplete="name"` |

- Label above every field. Placeholder is an example, never the label.
- Field limits come from `Domain/Common/FieldLimits.cs`; mirror them in UI validation (phases rule) [code].
- Use `ReturnType="Next"` to chain fields, and `Done` on the last one to submit [rec].
- Dirty form plus Back: confirm "Discard changes?" [rec, see handoff].

Second-audit copy: past due date is allowed with the inline warning "Due date is in the past." (`SemanticOverdue`); amount fields show `DecimalInput.Hint` on ambiguous input; discarding a never-synced customer with records asks "Discard customer and their records?" naming counts, accept "Discard all"; signing in as another account with unsynced changes asks first (`AuthPrompts.OtherAccountText`); storage full shows `LocalStorageErrors.StorageFullMessage`.

## 8. Measurement UX

- Use a template (`GarmentTemplate`) to pick fields, in the order they are measured on the body, top to bottom [rec].
- Large numeric fields (≥48 high), a unit next to each, and decimals allowed (1 dp).
- Range sanity [rec]: warn (don't block) when a value is outside a plausible range for the unit, e.g. "60 in for neck? Check the unit." (hard limits: > 0 and ≤ 300 cm / 118 in [code]) Never silently convert units.
- Show the last value and date as a hint when re-measuring [rec]. History uses `dd MMM yyyy` [code].
- Orders snapshot measurements; later edits don't change past orders (domain).

## 9. Search, filter, lists

- Search sits at the top and matches name **and** phone (users search by number). Search local data on mobile; the API uses the same keys (accent-folded word prefix, local or +233 phone digits) [code].
- Phones are stored as `+233XXXXXXXXX` (typed `024 555 0123` is fine); a bad format is an inline phone error, only for a new or changed number [code]. Adding a customer whose number is already saved asks "Same phone number — {names} already has this number. Save another customer anyway?" (**Save anyway** / **Cancel**); never blocks [code].
- Money is always formatted en-GH (`GH₵ 1,250.00`) via view-model/DTO properties, never XAML `StringFormat` (that follows the phone's language) [code].
- Pull-to-refresh syncs first (waits up to 15 s) and then shows what is on the device [code].
- **Search-as-you-type (one rule everywhere)** [code: `Application/Common/CustomerSearchKeys`, `Mobile.Data/Services/CustomerSearchFilter`, `Mobile/Helpers/SearchDebouncer`]: debounce **250 ms**, a newer keystroke cancels the stale query; results capped in SQL (picker 30). Name = case-insensitive **any word prefix** ("men" → Efua Mensah, "fua" does not). Phone = national-digit prefix, so `024`, `24 123`, `0244` and `+23324` all match `+233 24 …`; a single digit is ignored. LIKE wildcards in input are dropped. Customers tab, Orders list (customer part) and the New order picker share it.
- **Customer picker** (New order step 1) [code]: search field (Search icon, 48 high, white, `Gray300` border, clear `Close` button when non-empty, autofocus); empty query shows **Recent customers** (newest orders, then newest added); rows = initials avatar + name with the matched word prefix **bold `PrimaryDark`** (`FormattedString`) + phone formatted `+233 24 555 0123`; empty state `No customers match "x"` + **Add new customer** (opens Add customer prefilled with what was typed, returns with them selected); selected customer = card with **Change**. Pre-filled when started from Customer detail.
- Filters are chips (mobile) or a filter bar (web): status, due (Today / This week / Late), assignee.
- Use `CollectionView` (virtualised). Page Api lists server-side (web skill rule 8).
- Rows show the primary line (name/ref), a secondary line (phone/garment), and a trailing status/amount. Example: `ORD-1042 · Efua Mensah · 2× dress · Sewing · Due Fri · GH₵200 of 800 paid`.

## 10. Roles & visibility

- **Hide** what a role can't use. Don't show disabled items with no explanation. If a Tailor reaches a restricted page through a link, explain: "Only the owner or manager can record payments."
- Role labels come from `BusinessRole` (`Owner`/`Manager`/`Tailor`) [code]. Explanations: Owner "runs the business", Manager "runs the shop day to day", Tailor "sees work assigned to them".
- Solo businesses never see staff setup. Role-limited UI is [planned Slice 7].

## 11. Accessibility (TalkBack, scaling, focus)

- Font scaling: no fixed `HeightRequest` on text containers. Test at Android font size "Largest". Quick-action buttons are fixed at `HeightRequest="92"` [code], so verify labels don't clip.
- `SemanticProperties.Description` on icon-only controls; `SemanticProperties.HeadingLevel` on page titles (placeholder page sets Level1 [code]).
- Focus/reading order follows visual order. Put the error label right after its field in the XAML tree.
- Web: skip link exists [code `_Layout`]; `forced-colors` and `prefers-reduced-motion` media queries exist in site.css [code].
- Targets ≥44×44 (mobile) and visible focus ring (web).

## 12. Perceived performance on low-end Android

- First paint from local SQLite; never wait for the network to show a list.
- Avoid nested `StackLayout` inside `CollectionView` items, shadows, and large images. Flatten item templates.
- Load images (style photos) as thumbnails and lazily [rec].
- Make tap feedback immediate (pressed state / busy), even if the work takes time.
- Show a skeleton if a load takes over about 300 ms; no spinner flash under that [rec].

## 13. Legal pages

Terms and Privacy exist on both platforms (mobile `TermsPage`/`PrivacyPage`, web `/Terms`, `/Privacy`). They are linked from the Register terms row, mobile **More → About** (`MoreTermsLink`/`MorePrivacyLink` in `MorePlaceholderPage`, plus the real app version), and the web `_Layout` footer [code]. Keep wording in sync with `docs/legal`. Use `.nb-legal` (max-width 760px) on web and page padding 16 on mobile.
