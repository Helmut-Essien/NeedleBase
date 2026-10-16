---
name: needlebase-phases
description: >-
  Guides NeedleBase implementation slices (phases): scaffold, auth/tenancy,
  customers/measurements, orders, offline sync, payments, web oversight, staff,
  subscription. Use when planning delivery, starting a phase, asking what to
  build next, scoping MVP work, or confirming slice acceptance. Delivers one
  slice at a time per user confirmation. Learned from OrderFlow phases.
---

# NeedleBase Implementation Phases

Deliver **one slice at a time**. Confirm with the user before starting the next. Every slice must follow [engineering-review](../needlebase-architecture/engineering-review.md) and [code-quality-performance](../needlebase-architecture/code-quality-performance.md). UI must match [needlebase-uiux](../needlebase-uiux/SKILL.md). Domain/contracts: [needlebase-domain](../needlebase-domain/SKILL.md).

Learned from OrderFlow (`/home/helmut/Documents/My Projects/OrderFlow/.cursor/skills/orderflow/phases.md`): slice table + status, per-slice acceptance, full-stack in the same slice, stop and confirm before the next.

## Delivery rules

1. **One slice at a time** — full code, tests, config, then stop.
2. **Ask for confirmation** before the next slice.
3. No placeholders (`// TODO`, `// add logic here`).
4. **Full-stack in the same slice** — Domain + Application DTOs/use cases + Infrastructure/EF + Api (+ Mobile.Data/sync if offline + Mobile UI + Web if in scope). Never leave backend-only or client-only constraints for a field that ships in the slice.
5. Apply engineering review before coding; fix root cause; remove obsolete code; verify with evidence.
6. Performance by default on hot paths (BusinessId-indexed queries, `AsNoTracking` reads, pagination, compiled MAUI bindings, batched sync).
7. Slice scope and acceptance live in [phases.md](phases.md). Update **Status** in that table when a slice completes.

## Slice overview

| Slice | Focus | Journey gate | Status |
|-------|-------|--------------|--------|
| 0 | Scaffold: sln, Clean Architecture projects, README, health | — | Planned |
| 1 | Auth + Business tenancy: JWT/refresh, register business, roles claims | J1 start | Planned |
| 2 | Customers + measurements (local + API) | J1 | Planned |
| 3 | Orders + lines + production status (mobile primary) | J1 | Planned |
| 4 | Offline SQLite sync push/pull + DeletedItem + SyncChip honesty | J2 | Planned |
| 5 | Payments, balances, receipts; conflict-safe merge | J1 deposit + J4 | Planned |
| 6 | Web oversight: dashboard + production board (HTTP only) | J3 | Planned |
| 7 | Staff invites + role-filtered UI | J5 | Planned |
| 8 | Subscription/trial surfaces + polish (empty/error, indexes) | — | Planned |

Details and acceptance criteria: [phases.md](phases.md).

## How to work a slice

1. Read [phases.md](phases.md) for the active slice’s acceptance checklist.
2. Inspect existing code (engineering review) — do not reinvent parallel paths.
3. Implement Domain → Application → Infrastructure/Api → Mobile.Data/sync (if needed) → Mobile → Web (if in scope) → tests.
4. Run relevant tests/checks; state what remains unverified.
5. Mark the slice **Done** in [phases.md](phases.md) only when acceptance passes.
6. Stop and ask the user before starting the next slice.

## Out of scope until explicitly requested

Multi-currency runtime, multi-business switching UI, full web offline parity, native payment-provider confirmation as synced truth, AI features, Redis, rewriting Clean Architecture into a different style.

## Related skills

| Need | Skill |
|------|-------|
| Layers & DI | [needlebase-architecture](../needlebase-architecture/SKILL.md) |
| Entities & DTOs | [needlebase-domain](../needlebase-domain/SKILL.md) |
| Api | [needlebase-api](../needlebase-api/SKILL.md) |
| MAUI | [needlebase-mobile](../needlebase-mobile/SKILL.md) |
| Sync | [needlebase-mobile-sync](../needlebase-mobile-sync/SKILL.md) |
| Web | [needlebase-web](../needlebase-web/SKILL.md) |
| Journeys / IA | [needlebase-uiux](../needlebase-uiux/SKILL.md) |
