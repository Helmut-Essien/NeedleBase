---
name: needlebase-phases
description: >-
  NeedleBase slice status, acceptance lists, later work, and the code-gaps
  backlog. Most slices are built. Plan limits and the Free phone-only tier
  are decided but not built. Use when checking what is built, planning later
  work, confirming acceptance, or picking a code gap to fix. Confirm with the
  user before starting a new slice or later-work item.
---

# NeedleBase Implementation Phases

## Standing rules

Follow [standing-rules.md](../standing-rules.md) on every change in this repo: back up the file before you edit it, never touch `.vscode`, commit only when the user says "commit" (locally, no push, no amend or rebase, no `--no-verify`), never commit secrets, use `grep` not `rg`, treat Platform and AssetTag as read-only, and ship a clean Release `--no-incremental` build with 0 warnings and all tests passing. Product copy is plain language for tailors, seamstresses, dressmakers and fashion workshop owners — never "sewing shops".

Status and acceptance live in [phases.md](phases.md). The backlog of skill-vs-code gaps is [code-gaps.md](code-gaps.md). Confirm with the user before starting later work. Every slice follows [engineering-review](../needlebase-architecture/engineering-review.md) and [code-quality-performance](../needlebase-architecture/code-quality-performance.md). UI must match [needlebase-uiux](../needlebase-uiux/SKILL.md). Domain and contracts: [needlebase-domain](../needlebase-domain/SKILL.md).

## Delivery rules

1. **One slice at a time** — full code, tests, config, then stop.
2. **Ask for confirmation** before the next slice.
3. No placeholders (`// TODO`, `// add logic here`).
4. **Full-stack in the same slice** — Domain + Application DTOs/use cases + Infrastructure/EF + Api (+ Mobile.Data/sync if offline + Mobile UI + Web if in scope). Never leave backend-only or client-only constraints for a field that ships in the slice.
5. Apply engineering review before coding; fix root cause; remove obsolete code; verify with evidence.
6. Performance by default on hot paths (BusinessId-indexed queries, `AsNoTracking` reads, pagination, compiled MAUI bindings, batched sync).
7. Slice scope and acceptance live in [phases.md](phases.md). Update **Status** there when a slice completes. When a code gap is fixed, mark it Done (with date) in [code-gaps.md](code-gaps.md) and remove its inline marker.

## Where things are

- Slice table, status and acceptance criteria (single source): [phases.md](phases.md). Today S0–S3 are Done; S4 (Offline sync) is code-complete with automated tests (three audits fixed) and waits for on-device verification (Journey 2); S5 (Payments) follows.
- Code that doesn't meet the skills yet, ranked: [code-gaps.md](code-gaps.md). G1, G2, G4, G5, G6, G10 are done; G3, G7, G8, G9, G11 are deferred ops/scale items.
- Add-entity checklist: [needlebase-domain](../needlebase-domain/SKILL.md#add-entity-checklist-single-source).

## How to work a slice

1. Read [phases.md](phases.md) for the active slice’s acceptance checklist.
2. Inspect existing code (engineering review) — do not reinvent parallel paths.
3. Implement Domain → Application → Infrastructure/Api → Mobile.Data/sync (if needed) → Mobile → Web (if in scope) → tests.
4. Run relevant tests/checks; state what remains unverified.
5. Mark the slice **Done** in [phases.md](phases.md) only when acceptance passes.
6. Stop and ask the user before starting the next slice.

## Out of scope

See the end of [phases.md](phases.md).

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
