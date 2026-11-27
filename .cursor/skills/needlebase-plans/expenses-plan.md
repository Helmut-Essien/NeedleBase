# Expenses — implementation plan (plan step 7b-2)

**Status: planned, not started (27 Nov 2026).** Single source of truth for expenses. Other skills only point here. Do not start coding until the user says so. Follow [standing-rules.md](../standing-rules.md) and the [add-entity checklist](../needlebase-domain/SKILL.md#add-entity-checklist-single-source).

Why: step 7b reports show money in and owed, but the Terms and landing page sell "profit and monthly reports". Profit needs costs. Expenses close that gap before Workshop/Production are sold. Photos (7c) stay after this.

## 1. Product scope

**What it is:** a cash-basis log of money the shop paid out (fabric, staff pay, rent and bills, repairs, transport, other), optionally linked to one order. Monthly report: **Profit = money in − expenses** for the same Accra month. Unpaid balances and unrecorded costs are not counted (copy says so).

**Not in scope:** recurring expenses, receipts/photos of bills, suppliers, budgets, VAT, editing an expense (void and record again), per-staff payroll, multi-currency, cost-per-line on orders, expenses on Free.

### Plan gating (decision: share the reports entitlement)

| Shop | Expenses |
|------|----------|
| Workshop, Production | Yes |
| Complimentary, keyless (unlimited) | Yes |
| Starter, trial key (acts as Starter), unknown/blank plan (fails closed to Starter) | No: API 403 `EXPENSES_NOT_ALLOWED`; UI shows the upgrade message, no form |
| Free (phone only) | Hidden completely (no menu entry, no table rows created) |
| Lapsed / read-only (any plan) | View and export only. Writes blocked (`SubscriptionWriteGuard` → 403 `SUBSCRIPTION_READ_ONLY` on API; phone `LocalSubscriptionGate.IsReadOnly` → "Your plan has ended…" and no form) |

- Gate = existing `PlanEntitlements.ProfitAndMonthlyReports` surfaced as `SubscriptionStatusDto.CanReports`. **No new `CanExpenses` flag**: expenses only exist to make profit; one flag cannot drift. Doc comment on `CanReports` must say "monthly reports and expenses".
- **Downgrade** Workshop → Starter: existing expenses stay in the database, appear in export, are not listed via API (403) and not pulled by phones (pull phase skipped, cursor reset). Re-upgrade: cursor is null, so the next pull brings them all back. Nothing is deleted.
- **Free → paid upgrade:** Free cannot create expenses, so nothing to upload; `FreeUpgradeLocalService.RemapBusinessIdAsync` still lists `"Expenses"` in its table list (defensive, cheap).

### Roles

| Role | API | Phone | Web |
|------|-----|-------|-----|
| Owner | list, get, record, void | More → Money → Expenses; order detail section | Sidebar Expenses; order detail section |
| Manager | same as Owner | same | same |
| Tailor | 403 `EXPENSES_FORBIDDEN` on every route; sync pull skips phase; push → Failed `EXPENSES_FORBIDDEN` | no entry, no section; local rows purged on role change | no nav link; direct URL → access-denied page |

Role rule: `ExpenseRules.CanManage(role)` = Owner or Manager (same set as `PaymentRules.CanManage`; separate method so the rules can diverge later).

### Language and formats

- Screen title "Expenses"; subtitle "Money the shop paid out: fabric, staff pay, rent and other costs". Buttons: "Record expense", "Void expense". Report: "Spent", "Profit" (or "Loss" with the absolute value when negative, never a bare minus sign alone).
- UK spelling ("licence"), GH₵ via `Money.Format`, dates `dd MMM yyyy`, months `MMMM yyyy`, phones via `FormatPhone`. Never "sewing shops". No invented numbers in marketing.

### Categories (string codes, extensible like `PaymentMethod`)

| Code | Label |
|------|-------|
| `Materials` | Fabric and materials |
| `StaffPay` | Staff pay |
| `RentAndBills` | Rent, light and water |
| `Equipment` | Machines and repairs |
| `Transport` | Transport |
| `Other` | Other |

`Domain/Enums/ExpenseCategory.cs`: constants, `All`, `DisplayName(code)`, `IsKnown`. Unknown code on REST → 400 `EXPENSE_INVALID`; on sync push → mapped to `Other` with "[original]" appended to the note (same as `PaymentRules.NormalizeSyncMethod`). `FieldLimits.ExpenseCategory = 16`.

## 2. Domain and database

### `Domain/Entities/Expense.cs` (`Entity, IBusinessScoped`)

| Field | Type | Rule |
|-------|------|------|
| `Id` | string ULID (26) | client-generated; idempotency key |
| `BusinessId` | string (26) | tenant; fail-closed filter |
| `Amount` | decimal(18,2) | > 0, ≤ 50,000,000 (`Expense.AmountMax`), ≤ 2 decimals, never rounded (reuse the `Payment.CheckAmount` logic via a shared `MoneyRules.CheckAmount`) |
| `Category` | string (16) | one of `ExpenseCategory` |
| `SpentOn` | `DateOnly` | **Accra calendar date** the money left the shop |
| `Note` | string? (≤ `FieldLimits.Notes` 2000) | trimmed, control chars removed (`CleanText`), empty → null |
| `OrderId` | string? (26) | optional; must be an order of the same shop |
| `RecordedByUserId` | string? (≤ `FieldLimits.AssignedToUserId` 450) | server sets from the caller (REST and sync); null for rows from before accounts (never on Free) |
| `IsVoided` | bool | one-way |
| `VoidedAtUtc` | DateTimeOffset? | |
| `VoidedByUserId` | string? (450) | server sets |
| `VoidReason` | string? (≤ Notes) | required on REST void (min 3 chars after trim), lenient on sync |
| `CreatedAtUtc`, `DateModifiedUtc` | DateTimeOffset | `SyncTime.Normalize` |
| shadow `SyncModifiedUtc`, `SyncTxId` | | same as other synced entities |

**Date decision:** `SpentOn` is a `DateOnly` in Accra time, not a UTC instant. The user picks a date ("I paid rent on 1 Nov"), not a moment; a date has no timezone shift, so month grouping is exact. Payments are grouped by the Accra date of `RecordedAtUtc` (`BusinessTime`), so both land in the same Accra month. Rules: `SpentOn` ≥ 2020-01-01 (`ReportRules.EarliestMonth`) and ≤ today in Accra (REST 400 `EXPENSE_INVALID`, "The date can't be in the future"); sync push clamps a future date to today (Accra) and keeps the row.

Methods: `Create(id, businessId, amount, category, spentOn, note, orderId, recordedByUserId, utcNow)`, `Void(reason, voidedByUserId, utcNow)` (idempotent: already voided → no change, first reason wins), `DetachOrder(utcNow)` (only used by sync, see 5). No update method (append-only).

### EF (`ApplicationDbContext`)

- `DbSet<Expense> Expenses`; table `Expenses`; precision (18,2); max lengths from `FieldLimits`; `SpentOn` as Postgres `date`.
- Indexes: `(BusinessId, SpentOn)`, `(BusinessId, OrderId)` (filtered `OrderId IS NOT NULL`), plus the shared sync indexes `(BusinessId, SyncModifiedUtc, Id)` and `(BusinessId, SyncTxId, Id)` by adding `typeof(Expense)` to the synced-type loop.
- FKs: Business cascade; Order **NO ACTION** (same as payments). Order delete with linked expenses → refused (see 3). Query filter `CurrentBusinessId != null && x.BusinessId == CurrentBusinessId`.
- Add `Expense => SyncEntityType.Expense` to the SaveChanges sync-type switch (stamps `SyncModifiedUtc`).
- Add `UPDATE "Expenses" SET "SyncTxId" = …` to `BumpSyncEpochAsync`.
- Migration `Expenses`: table, indexes, `SyncTxId` column, trigger `trg_expenses_sync_tx` copied from the Payments migration; `Down` drops trigger and table. Regenerate `migrations.sql` per `docs/deployment.md`.
- `AuditCapture`: `case Expense e: ExpenseEvents(...)`: Added → `expense.recorded` "Recorded expense GH₵ 120.00 (Fabric and materials)"; Modified with `IsVoided` true → `expense.voided` "Voided expense … — reason". Note text never logged.

## 3. Application

- `Domain/Enums/SyncEntityType`: `Expense = "expense"` (+ `All`).
- `Application/Expenses/ExpenseRules.cs`: `CanManage(role)`, `CheckEntry(request, todayAccra, strict)`, codes/messages: `NotAllowedMessage` ("Expenses come with the Workshop and Production plans. Upgrade to see your profit after costs."), `ForbiddenMessage`, `VoidReasonMin = 3`.
- `Application/Contracts/Expenses/ExpenseDtos.cs`:
  - `RecordExpenseRequest(Id?, Amount, Category, SpentOn, Note?, OrderId?)`
  - `VoidExpenseRequest(Reason, BaseModifiedUtc?)`
  - `ExpenseDto(Id, Amount, Category, CategoryLabel, SpentOn, Note, OrderId, OrderDisplayRef?, CustomerName?, RecordedByName?, IsVoided, VoidedAtUtc, VoidReason, VoidedByName?, CreatedAtUtc, DateModifiedUtc)`
  - `ExpenseListQuery(Month?, Category?, OrderId?, IncludeVoided=true, Page=1, PageSize=50 max 200)`
  - `ExpenseListResponse(PaginatedResponse<ExpenseDto> Items, decimal Total (non-voided, whole filter), int Count, IReadOnlyList<CategoryTotalDto> ByCategory)`
  - `CategoryTotalDto(Category, Label, Total, Count)`
- Validators (FluentValidation, same limits as Domain): amount, category known, SpentOn range, note ≤ 2000, OrderId ≤ 26, reason 3–2000.
- `Infrastructure/Expenses/ExpenseService.cs` (`IExpenseService`): 
  - entitlement check first (`ReportAccess` helper extracted from `MonthlyReportService`: resolve plan → `ProfitAndMonthlyReports`) → `ExpensesNotAllowed`; role → `ExpenseForbidden`.
  - Create: id = request.Id ?? Ulid; global lookup with `IgnoreQueryFilters` — same business + same content → return existing (200), else `ExpenseConflict` 409; OrderId must exist in the shop (`ExpenseOrderInvalid` 400; cancelled/collected orders allowed); advisory lock on id (`pg_advisory_xact_lock(hashtext(id))`) to make concurrent replays safe.
  - List: AsNoTracking, `SpentOn` within the Accra month (`ReportRules.TryResolveMonth`), category, orderId (no month needed), includeVoided; order by SpentOn desc, CreatedAtUtc desc, Id; totals computed by a grouped query over the same filter (voided excluded). Names joined from staff/orders in one round trip.
  - Get, Void (idempotent; `BaseModifiedUtc` mismatch → `Stale` 409 with fresh DTO).
  - Logs `access.expenses_viewed` (throttled 1 h, like payments) on list.
- `RecordError` additions: `ExpensesNotAllowed`, `ExpenseForbidden`, `ExpenseNotFound`, `ExpenseConflict`, `ExpenseInvalid`, `ExpenseOrderInvalid`. Order delete: `OrderService.DeleteAsync` refuses when linked expenses exist → `OrderHasExpenses` (409 `ORDER_HAS_EXPENSES`, "This order has expenses recorded against it. Void them or keep the order."); sync order delete → existing `DeleteRefused` conflict with the same message.
- `AuditActions`: `ExpenseRecorded = "expense.recorded"`, `ExpenseVoided = "expense.voided"`, `ExpensesViewed = "access.expenses_viewed"`, labels, `IsExpense`, activity filter group `("expense.", "Expenses")`. Activity shows to Owner and Manager (existing audit policy).

### Reports (`MonthlyReportCalculator`, additive)

- Input `ReportExpenseRow(Amount, Category, SpentOn, IsVoided)`; voided ignored.
- `MonthlyReportDto` adds: `Spent`, `ExpenseCount`, `Profit` (= MoneyIn − Spent, may be negative), `PreviousProfit`, `ProfitChangeDisplay`, `IsLoss`, `SpentByCategory: IReadOnlyList<CategoryTotalDto>` (sorted by total desc, zero categories omitted), `HasExpenses`.
- `MonthTrendDto` adds `Spent`, `Profit` for the 6-month trend.
- `IsEmpty` = no payments, no orders and no expenses.
- Replace `ReportRules.CostsNote` with `ProfitNote`: "Profit is money in minus the expenses you recorded for the month. Money still owed and costs you haven't recorded aren't counted." When the month has no expenses: "No expenses recorded this month, so profit equals money in."
- Same calculator on server (`MonthlyReportService` loads expenses for the 6-month window by `SpentOn`) and phone (`ReportLocalService`). New fields are additive; old phones ignore them.

### Export (`BusinessExportService`)

Add `expenses` array (all rows incl. voided: id, amount, category, categoryLabel, spentOn yyyy-MM-dd, note, orderId, recordedByUserId, isVoided, voidedAtUtc, voidedByUserId, voidReason, createdAtUtc). Works while read-only (export is `[AllowWhenReadOnly]`) and on Starter after downgrade (export is data the shop owns).

### Plan emails (`PlanEmailService` / `EmailTemplates.MonthlySummary`)

Decision: for shops with `ProfitAndMonthlyReports`, the monthly Owner summary adds "Money in", "Spent" and "Profit/Loss" for the month just ended (computed with the same calculator), plus a link to `/Reports`. Starter emails unchanged. Weekly summary unchanged.

## 4. API (`ExpensesController`, `[Authorize(Policy = "RequireStaff")]`, role check in service)

| Method & route | Success | Errors |
|----------------|---------|--------|
| `GET /api/expenses?month=yyyy-MM&category=&orderId=&includeVoided=true&page=1&pageSize=50` | 200 `ExpenseListResponse` (works read-only) | 400 `EXPENSE_FILTER_INVALID` (bad month/category/page), 403 `EXPENSES_NOT_ALLOWED`, 403 `EXPENSES_FORBIDDEN` |
| `GET /api/expenses/{id}` | 200 `ExpenseDto` | 404 `EXPENSE_NOT_FOUND` (also other shop's id), 403 as above |
| `POST /api/expenses` | 201 `ExpenseDto` + Location; 200 on identical replay | 400 `EXPENSE_INVALID` / `VALIDATION_FAILED`, 400 `EXPENSE_ORDER_INVALID`, 409 `EXPENSE_CONFLICT`, 403 `SUBSCRIPTION_READ_ONLY`, 403 as above |
| `POST /api/expenses/{id}/void` body `{ reason, baseModifiedUtc? }` | 200 `ExpenseDto` (also when already voided — idempotent like payments, so no `EXPENSE_ALREADY_VOIDED`) | 400 `EXPENSE_INVALID` (reason), 404, 409 `STALE_UPDATE`, 403 read-only/plan/role |
| `GET /api/reports/monthly` (existing) | additive fields from 3 | unchanged |
| `DELETE /api/orders/{id}` (existing) | | new 409 `ORDER_HAS_EXPENSES` |

All errors via `ApiProblems.ForExpense` (ProblemDetails with `code`, plain `detail`). Rate limiting: existing per-user policy. Order of checks: auth → plan → role → validation → existence.

## 5. Sync (server `SyncService` + `Mobile.Data`)

### Contracts

- `SyncExpenseDto(Id, Amount, Category, SpentOn, Note, OrderId, IsVoided, VoidedAtUtc, VoidReason, CreatedAtUtc, DateModifiedUtc)` (user ids are server-only).
- `SyncPushOperation.Expense`, `SyncPushResult.Expense`, `SyncPullResponse.Expenses` (init; null from older servers).
- `SyncErrorCodes`: `ExpenseMismatch = "SYNC_EXPENSE_MISMATCH"`, `ExpensesNotAllowed = "EXPENSES_NOT_ALLOWED"`, `ExpensesForbidden = "EXPENSES_FORBIDDEN"`.

### Server push (`ApplyExpenseAsync`)

- Upsert only; `delete` → Failed `SYNC_PAYLOAD_INVALID`.
- Plan off → Failed `EXPENSES_NOT_ALLOWED`; Tailor → Failed `EXPENSES_FORBIDDEN` (phones normally never send these, see engine rules).
- Lenient entry: category normalised, future date clamped, amount invalid → Failed `SYNC_PAYLOAD_INVALID`.
- New id → create (RecordedBy = caller). Same id + same content → Applied, merging a void from either side (void wins; first reason kept; VoidedBy = first voider). Same id + different amount/category/date/order → Conflict `SYNC_EXPENSE_MISMATCH` with the server copy (never overwritten).
- `OrderId` unknown/deleted on the server → keep the expense, store `OrderId = null`, append "[order removed]" to the note, Applied **with the server copy** so the phone adopts it. (A money record is never lost because an optional link broke.)
- Read-only shop: push still applies (offline work must land), matching payments.

### Server pull

- New phase `PhaseExpenses = 5` appended after payments; `PhaseLast = PhaseExpenses`; cursor JSON key `x`; `CurrentVersion` stays 2 (old cursors simply have no `x`).
- Phase skipped with `cursor.Expenses = null` when the role can't manage or the plan lacks the entitlement (so a later promotion/upgrade pulls everything).

### Phone (`Mobile.Data`)

- `LocalDbContext`: `DbSet<Expense>`, same config, `SpentOn` stored as TEXT `yyyy-MM-dd` (EF converter; sorts and compares correctly), `ServerKnown` column + `IX_Expenses_Unknown` filtered index, indexes `(BusinessId, SpentOn)`, `(BusinessId, OrderId)`. Order FK: none locally (link is optional; a missing order shows "Order removed").
- `LocalSchemaMigrator` **version 13** = Expenses table + indexes (forward-only; v12 is the latest today).
- `SyncQueueWriter`: queue `Expense` upserts only (label "Expense GH₵ 120.00"), ParentId = OrderId when set.
- `SyncEngine`: push sort rank after payments; adoption (`UnadoptedExpenses`/`UnqueuedExpenses`, `MarkExpensesKnownAsync`); **skip pushing expense ops while `LocalSubscriptionGate.Current?.CanReports != true` or role is Tailor** (they stay Pending with Sync details text "Waiting for the Workshop plan" / removed on Tailor purge); apply pulled/result copies with `SuppressSyncQueue`; deferred apply when the linked order isn't local yet (`SyncDeferred`, released after orders); resync prune list includes expenses not in `seen` and not queued; conflict text for `SYNC_EXPENSE_MISMATCH`: "A different expense with this id was saved elsewhere. Keep both or keep the saved one." (Keep mine = re-record with a new id; Keep theirs = drop local).
- Order deleted on server with a local unsynced expense linked: the server refuses deletes with linked expenses; for the remaining race the push rule above detaches the link.
- Tailor role change: `IExpenseLocalService.PurgeHiddenAsync` (non-queued rows) + `ResyncPending`, same trigger as payments.
- Sign-out: `StopAsync` then `LocalDatabaseService.WipeAsync` deletes the database file — expenses included, no extra code; add a test assertion.
- `LocalDataVersion.NotifyChangedBySync` already fires; Expenses/Reports/Order detail view models subscribe and reload.
- `SyncStatusViewModel` / Sync details: entity label "Expense", states for the codes above.

## 6. Phone screens (MAUI)

All new pages: XAML with compiled bindings (`x:DataType`) or code-only `Ui` builders matching neighbours (ReportsPage is code-only; forms follow `RecordPaymentPage`). Routes registered in `AppShell`, DI in `MauiProgram`. 48 dp targets, `SemanticProperties` on every control, status = colour + text + icon, SyncChip in the header.

1. **More → Money** section: "Monthly reports" (existing) + **"Expenses"** (Owner/Manager, not Free). Starter/trial/unknown: tapping shows the upgrade message page state (same as reports). Tailor: hidden.
2. **ExpensesPage** (`ExpensesViewModel`): month switcher (‹ November 2026 ›, no future months, back to Jan 2020); summary card "Spent this month GH₵ X · N expenses"; category chips (All + six, horizontally scrolling, selected = filled); list rows: category icon + label, amount, `dd MMM yyyy`, note first line, order ref chip ("#A1B2 · Efua Mensah") when linked, badges "Not synced yet" (queued) and "Voided" (strikethrough amount, grey); pull-to-refresh triggers sync; toolbar/primary button "Record expense" (hidden when read-only or not allowed); states: loading skeleton, empty ("No expenses in November 2026. Record fabric, staff pay, rent and other costs to see your profit."), filtered empty, offline banner (list still works from SQLite), read-only banner, not-allowed state. Tap row → detail.
3. **RecordExpensePage** (`RecordExpenseViewModel`, query `orderId` optional): Amount (numeric keyboard, GH₵ prefix, validation messages from `ExpenseRules`), Category (picker or six chips, required), Date (DatePicker, default today Accra, max today, min 01 Jan 2020, shown `dd MMM yyyy`), Note (editor, counter at 2000), Order (optional: "Link to an order" → order search picker reusing the order list search; prefilled and locked-but-removable when opened from an order), Save (disabled while saving; double-tap safe via client ULID created once per page), Cancel with unsaved-changes prompt (`IBackAware`). On save: toast "Saved on this device" when offline/queued, "Expense recorded" when synced; back to the previous page. Errors inline + summary for screen readers.
4. **ExpenseDetailPage** (`ExpenseDetailViewModel`): amount, category, date, note, linked order (tap → OrderDetailPage, or "Order removed"), recorded by + when, sync badge; voided block (reason, who, when). "Void expense" (Owner/Manager, not voided, not read-only) → sheet with reason (required, 3+ chars) and confirm "Void GH₵ 120.00? It stays in your records and stops counting toward profit." Offline void queues and shows "Not synced yet".
5. **OrderDetailPage**: new "Expenses for this order" section (Owner/Manager + plan): total spent on this order, up to 3 latest rows, "See all" (ExpensesPage filtered by order, month switcher hidden), "Add expense" (opens RecordExpensePage with orderId). Order delete confirmation text explains "has expenses" refusal.
6. **ReportsPage** updated: cards Money in · Spent · Profit/Loss (with change vs last month), "Spent by category" bars (text + amount, not colour only), 6-month trend showing in/spent/profit per month, `ProfitNote`, link "See expenses" → ExpensesPage for that month. Live refresh on sync.
7. **Home**: no new quick action (keeps four). Free: nothing changes.
8. Update `needlebase-uiux` [information-architecture](../needlebase-uiux/information-architecture.md), [mobile-screens](../needlebase-uiux/mobile-screens.md), [content-and-states](../needlebase-uiux/content-and-states.md) in the same change.

## 7. Web pages (Razor, HTTP-only to the API, strict CSP, no inline styles/scripts)

1. **Sidebar**: "Expenses" under Reports for Owner/Manager (`User.CanSeePayments()`), with Material icon; Tailor never sees it.
2. **`/Expenses`** (`Pages/Expenses/Index`): month selector (‹ › links + `<input type="month">` GET form), category filter (select), "Include voided" checkbox, summary strip (Spent, count, top category), table (Date · Category · Amount · Note · Order · Recorded by · Status) collapsing to cards below md, pagination, row "Void" button → `/Expenses/Details/{id}`. States: empty, filtered empty, not-allowed (upgrade text + link to Licence page for Owner), read-only banner (no Record/Void buttons), API error banner.
3. **`/Expenses/Create`** (`?orderId=` prefill): amount, category select, date (`type=date`, max today), note (maxlength 2000), order picker (search box → select of recent open/collected orders; prefilled read-only label with "Remove link" when orderId given); antiforgery; hidden client ULID generated on GET so a double submit is idempotent; server validation messages mapped from ProblemDetails; success → redirect to `/Expenses?month=` with TempData "Expense recorded".
4. **`/Expenses/Details/{id}`**: all fields, linked order link, voided info, Void form (reason textarea required, confirm checkbox "I understand this stays in the records", POST with `baseModifiedUtc`; `STALE_UPDATE` shows the fresh copy).
5. **`/Orders/Details`**: "Expenses for this order" card (total + rows + "Add expense" → `/Expenses/Create?orderId=`), Owner/Manager + plan only.
6. **`/Reports`**: Spent and Profit/Loss tiles, by-category table, trend columns, new note, link to `/Expenses?month=`.
7. **`/Activity`**: "Expenses" filter group.
8. **Landing / Terms**: `LandingSalesInfo` Workshop highlight can become "Monthly reports with profit after costs" only once built. Terms/Privacy: **flag for the user, do not rewrite** — Terms "Business Data" definition and the Privacy "information we hold" list mention payment records but not expense records (notes may name staff, e.g. "Kofi's pay"). Suggested wording is for the user/lawyer to approve.
9. `INeedleBaseApi`: `GetExpensesAsync`, `GetExpenseAsync`, `RecordExpenseAsync`, `VoidExpenseAsync`.

## 8. Tests and gates

| Project | Tests |
|---------|-------|
| Domain.Tests | `ExpenseTests`: create rules (amount bounds/decimals, category, date range, note clean), void idempotent/first reason wins, DetachOrder |
| Application.Tests | `ExpenseRulesTests` (strict vs lenient, category normalise, future-date clamp), `MonthlyReportTests` additions (profit, loss, voided ignored, by-category order, trend, Accra month edges 31 Oct/1 Nov, empty), `PlanCatalogTests` unchanged |
| Api.Tests | `ExpensesApiTests`: record 201/replay 200/conflict 409; list filters + totals + pagination; get 404 other shop; void 200 twice, stale 409; Tailor 403 all routes; Starter/trial/unknown 403 `EXPENSES_NOT_ALLOWED`; keyless/complimentary allowed; invalid/foreign orderId 400; read-only POST 403, GET 200; order delete 409 `ORDER_HAS_EXPENSES`; audit rows recorded/voided/viewed (throttled); export contains voided + live; `ReportsApiTests` profit fields; `ExpenseSyncTests`: push create/replay/void merge/mismatch/plan/Tailor/delete-op/unknown order detach, pull phase 5 order + Tailor/Starter skip + cursor reset + old cursor without `x` |
| Mobile.Tests | `ExpensesMobileTests`: offline record → reopen → one row after sync; queue skipped while plan off; void offline; Tailor purge; v12→v13 migration; Free remap; sign-out wipe; `ReportLocalService` profit equals server calculator; view-model validation messages |
| Web.Tests | `ExpensesPageTests` (render, not-allowed, read-only hides forms, Tailor denied, prefilled orderId), `ReportsPageTests` updated, nav test |

**Journey J6** (see [journeys](../needlebase-uiux/journeys.md)).

**Gates before calling it done:** clean `dotnet build -c Release --no-incremental -warnaserror` for the solution (Android head as far as the known XA0033 allows), full `dotnet test`, migration applies on a fresh and an existing database, Ralph Balanced pass, J6 on emulator + browser. Then update statuses here, in [SKILL.md](SKILL.md) step 7, [phases.md](../needlebase-phases/phases.md) and the pointers.

## 9. Build order (one PR-sized chunk at a time, stop for the user between chunks if asked)

1. Domain (`Expense`, `ExpenseCategory`, limits, `MoneyRules`) + tests.
2. Infrastructure EF + migration + audit capture + epoch restamp.
3. Application/Infrastructure service, validators, `RecordError`/`ApiProblems`, order-delete guard, controller + API tests.
4. Reports calculator/DTO + export + plan email + tests.
5. Sync contracts, server push/pull + tests.
6. Mobile.Data (v13, queue, engine, local service, purge, remap) + tests.
7. Phone screens (More, list, record, detail, order section, reports).
8. Web pages (nav, list, create, details, order card, reports, activity) + tests.
9. uiux docs, landing copy (after the user agrees), J6, gates.

## 10. Open questions (answer before coding)

1. Category list and labels above — keep, rename or add (e.g. "Tailoring outsourced")?
2. Monthly summary email gets profit (planned) — and should the weekly one too?
3. Allow recording on the website, or phone only? (Plan: both, since Owners do bookkeeping on a computer.)
4. Future dates: block (planned) or allow for planned costs?
5. Approve flagging Terms "Business Data" / Privacy list to add "expense records" (your/lawyer's wording).
6. Change the landing Workshop line to mention profit after costs once built?
