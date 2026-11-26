# Standing rules (NeedleBase)

These rules apply to every change in this repository. If a later instruction conflicts with them, follow these rules and ask the user.

## Before you edit

- Back up every file you will change to `~/.cache/needlebase-ci/backups/<task-name>-<timestamp>/`, preserving relative paths, before the first edit. Use a new folder for a new task.
- Never touch `.vscode`.
- Use `grep`, not `rg`.
- Platform and AssetTag are read-only. Do not change those systems from this repo.

## Commits and secrets

- Commit only when the user says "commit". Commit locally on the user's computer. Do not push. Do not amend or rebase. Do not use `--no-verify`.
- Never commit secrets, user-secrets, `appsettings` files that contain secrets, `bin/` or `obj/`, databases, keystores, or anything under `~/.cache`.

## Build and tests

- After code changes, ship a clean Release build with 0 warnings:

```bash
dotnet build NeedleBase.sln -c Release --no-incremental -warnaserror
```

- All tests must pass.

## Product copy

- Write in plain language for tailors, seamstresses, dressmakers and fashion workshop owners.
- Never say "sewing shops".
- Never imply the app runs inside WhatsApp.
- Do not invent testimonials, statistics, ratings or customer counts.
- Keep published Privacy Policy and Terms wording in sync across `docs/legal/`, the Razor pages and the MAUI pages. Do not invent new legal promises. If the product has changed and the legal pages need a new clause, flag it for the user instead of rewriting those pages quietly.

## Words we use

Use these terms consistently in skills and docs:

| In prose | In code and UI chrome | Meaning |
|----------|----------------------|---------|
| **shop** | `Business`, `BusinessId` | The customer's workshop. Say **shop** in prose. Use `Business` only when naming the entity or the tenancy filter. |
| **licence**, **licence key** | `License`, `LicenseKey`, `/Settings/License` | UK spelling in prose (same as the landing page and the legal documents). American spelling only for the in-app **License** page, its route, and code names. |
| **staff** | `StaffMembership` | People in the shop. Roles are **Owner**, **Manager** and **Tailor**. Prefer "staff" over "member". |
| **order** | `Order`, `OrderLine` | A customer's job. The production step named `Sewing` stays `Sewing`. |

## Pricing and plans (until built)

Plan prices and limits are decided. PlanCatalog, server staff caps, Free mode, upgrade from Free, export/usage display and paid emails (steps 1–6) are built. Step 7 is in progress (PDF receipts done; customer email channel for status+receipts with PDF attachment and opt-in; reports next; photos later via Cloudflare R2). Do not start photos or a later plan step until the user asks. Privacy/Terms describe customer email (updated 26 November 2026). The public landing-page prices show the decided prices (updated 18 November 2026). Do not change those public prices or the published Terms again unless the user asks. Automatic WhatsApp template messages are not a sold plan feature until Meta verifies the business; tap-to-send is the current path; paid-plan notifications are email. The decided prices, limits, PlanCatalog rules and implementation order are in [needlebase-plans](needlebase-plans/SKILL.md). A short price table also lives in the README section [Pricing (decided; Free + upgrade built)](../../README.md#pricing-decided-free--upgrade-built).

## Machine

The repo lives on the user's computer. If that machine is offline, stop and report it. Do not clone the repo onto another machine as a substitute.
