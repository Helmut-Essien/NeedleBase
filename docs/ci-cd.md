# CI/CD (GitHub Actions)

| File | What |
|---|---|
| `.github/workflows/ci.yml` | Every PR and push to other branches (main runs it inside Release): detect changes, build + test only the affected parts, Android Debug build, vulnerable-package check. |
| `.github/workflows/release.yml` | Every push to main (or manual run): auto version + tag, CI, signed APK + update site, Api/Web packages, idempotent migration SQL, GitHub Release, deploy to MonsterASP, optional Google Play upload. |
| `.github/scripts/detect-changes.sh` | The one rule set for "what changed" (used by both workflows; runnable locally). |
| `.github/actions/setup-dotnet-maui` | .NET 9 SDK + maui-android workload + NuGet, cached. |
| `.github/scripts/iis-deploy/` | Web Deploy to the one MonsterASP site: `Deploy-Site.ps1` (direct msdeploy: deploy, backup, restore), `WebDeploy.psm1` (package checks, skip rules, endpoint and connection parsing), `Wait-Healthy.ps1`. Tests: `.github/scripts/test-iis-deploy.ps1`, run by `test-scripts.sh`. |

## What builds when

`detect-changes.sh <base>` compares with the PR base, the previous push, or (releases) the previous `v*` tag:

| Changed | core (Domain+Application tests) | api | web | mobile |
|---|---|---|---|---|
| `src/NeedleBase.Domain`, `src/NeedleBase.Application` (+ their tests) | yes | yes | yes | yes |
| `src/NeedleBase.Api`, `src/NeedleBase.Infrastructure`, `tests/NeedleBase.Api.Tests`, `docker-compose.yml` | | yes | | |
| `src/NeedleBase.Web`, `tests/NeedleBase.Web.Tests` | | | yes | |
| `src/NeedleBase.Mobile`, `src/NeedleBase.Mobile.Data`, `tests/NeedleBase.Mobile.Tests` | | | | yes |
| `.github/`, `scripts/`, `*.sln`, `Directory.Build.*`, `.config/`, NuGet config, `global.json`, any unlisted file, unknown base | yes | yes | yes | yes |
| `docs/`, `*.md`, `.cursor/` only | | | | |

Try it: `.github/scripts/detect-changes.sh origin/main`.

**Branch protection:** require only the **"CI result"** check. Skipped jobs count as passed, a failed or cancelled
job fails it, so docs-only PRs can still merge.

Each push to main builds/deploys only the parts changed since the last release tag: an Api-only push deploys the Api and applies
migrations, leaves the Web alone and builds no APK. A manual run builds and deploys everything.

## One-time setup

Same shape as AssetTag's `main.yml`: one database, one secret for it, and nothing to type into the MonsterASP panel.

1. **Environment `production`** (Settings → Environments) holds the deploy and database secrets below (only the
   deploy job can read them). No reviewers needed; add yourself as *Required reviewer* only if you want to approve
   each deploy.
2. Repository **variables** below (at least `SITE_URL`).
3. **GitHub Pages** (in-app updates, [android-release.md](android-release.md#updates-in-app-sideload-builds)):
   Settings → Pages → Source **GitHub Actions**.
4. Optional: branch protection on main requiring "CI result"; environment `play-store` when you move to Play.

### Secrets

| Name | Where | Value |
|---|---|---|
| `ANDROID_KEYSTORE_BASE64` | repo | `base64 -w0 needlebase-upload.jks` (the one upload keystore, forever) |
| `ANDROID_KEYSTORE_PASSWORD` | repo | keystore password |
| `ANDROID_KEY_ALIAS` | repo | key alias, e.g. `needlebase` |
| `ANDROID_KEY_PASSWORD` | repo | key password |
| `PROD_DB_MIGRATION_CONNECTION_STRING` | production | **The** database connection (MonsterASP shows it on the database page): URI `postgresql://user:PASS@HOST:5432/DB?sslmode=require` or Npgsql `Host=..;Database=..;Username=..;Password=..`. Used to run the migration SQL **and** written into the Api's `web.config` as `ConnectionStrings__DefaultConnection`. |
| `MONSTER_SERVER` | production | Web Deploy server from the panel: bare host, `host:port` or `https://host:8172/msdeploy.axd` |
| `MONSTER_SITE` | production | IIS site name (e.g. `site12345`) |
| `MONSTER_USER` / `MONSTER_PASSWORD` | production | Web Deploy login of the site. No commas or double quotes (msdeploy can't parse them). |
| `JWT_SECURITY_KEY` | production, optional | ≥ 32 random characters. Unset = derived from the database secret (stable; changing the DB password signs everyone out once). |
| `PLATFORM_INTEGRATION_KEY` | production, optional | Platform licensing key; with the `PLATFORM_BASE_URL` variable turns license keys on. Unset = shops register without a key (warning in the run). |
| `PLAY_SERVICE_ACCOUNT_JSON` | play-store | Play Console service-account JSON (later) |

The deploy masks every value it writes. Optional extras (email, WhatsApp, proxies) are not wired: the deploy
replaces `/api/web.config`, so add them to the workflow step *Write server settings* if you need them
([production-config.md](production-config.md)).

### Variables

| Name | Default | Meaning |
|---|---|---|
| `SITE_URL` | (required) | `https://your-host/`, the site root. Baked into the Android app, used for the health checks and written into the packages as Web `Api__BaseUrl` and Api `FrontendBaseUrl`. |
| `SITE_HTTPS_PENDING` | off | **First deploy only**, before the host has enabled HTTPS: `true` turns the health checks into http smoke checks that warn and never roll back. Delete it once HTTPS works ([deployment.md](deployment.md)). |
| `MONSTER_ALLOW_UNTRUSTED` | off | `true` adds msdeploy `-allowUntrusted` (only if the Web Deploy endpoint has no valid certificate). |
| `VERSION_CODE_OFFSET` | 0 | Added to the run number for the Android versionCode. |
| `UPDATE_BASE_URL` | `https://<owner>.github.io/<repo>/` | Update site address; the APK reads `<this>update.json`. Set before the first release. |
| `ANDROID_MIN_SUPPORTED_VERSION_CODE` | 0 | Builds below this versionCode must update. |
| `PLATFORM_BASE_URL` / `PLATFORM_TOKEN_ISSUER` | unset | Optional, with `PLATFORM_INTEGRATION_KEY`. |
| `PLAY_UPLOAD_ENABLED` | off | `true` = also build the AAB and upload it to Play. |
| `PLAY_TRACK` / `PLAY_RELEASE_STATUS` | `internal` / `completed` | Play upload options. |

No longer used (delete if set): `DEPLOY_ENABLED`, `MIGRATIONS_MANUAL`, `ANDROID_BUILD_AAB`, `UPDATES_PAGES_ENABLED`.

## Releasing

Like AssetTag: **push to `main`, that's it.** No `git tag`, no version typing.

```bash
git push origin main
```

- **Version:** `NeedleBaseVersion` in `src/NeedleBase.Mobile/NeedleBase.Mobile.csproj` (now `0.1.0`). The first
  release uses it; after that the patch goes up by itself (`0.1.1`, `0.1.2`, ...; `.github/scripts/next-version.sh`).
  Edit the csproj only to jump major/minor (e.g. `1.0.0`). versionCode = run number + `VERSION_CODE_OFFSET`.
- **Tag + GitHub Release:** created by the workflow (`v<version>`, at the pushed commit) after the deploy succeeded.
  A failed run leaves no tag, so the next push includes those changes again. Workflow-made tags never start a
  workflow, and Release doesn't run on tags: no loops.
- **One run per push:** CI runs inside the Release workflow; `ci.yml` runs only for pull requests and other branches.
  Pushes that touch only `docs/`, `*.md` or `.cursor/` start nothing.

Each push (back up the database first if it has migrations):

1. CI (affected parts) → signed APK with in-app updates and/or Api/Web packages + migration SQL.
2. Deploy: migration SQL (one transaction; nothing deployed if it fails) → server settings written into the Api
   package → Api into `/api` → `/api/health/ready` must answer 200 → Web into `/` (the `/api` folder is skipped) →
   `/` must answer 200. A failed step restores the previous files (see *Failure behaviour*).
3. Tag `v<version>` + GitHub Release with the files and `SHA256SUMS` (0.x versions are pre-releases).
4. Android changed → GitHub Pages update site (download page, `update.json`, APK); installed apps offer the update.
   The commit message becomes the release notes. Play upload if enabled.

Manual run (Actions → Release → Run workflow): from `main` it rebuilds and redeploys **everything** as a new
version; from an old tag `v1.2.3` it redeploys that version (rollback).

### Server packages

Framework-dependent, `UseAppHost=false`, out-of-process hosting. One site ([deployment.md](deployment.md#one-site-web-at--api-at-api)):
Web at the root, Api in the `/api` sub-application. Web Deploy removes stale files (so old DLLs never linger) but
**never touches `App_Data`** (Data Protection key rings), `logs`, `data` or search-engine verification files, and
the Web deploy **never touches `/api`** (msdeploy skip rule). Checked before the deploy (job *server*): each
`web.config` keeps `system.webServer` inside `<location path="." inheritInChildApplications="false">` (so the Web's
handler never leaks into `/api`), no `App_Data` or `appsettings.Development.json`, and nothing named `api` in the
Web package.

### Failure behaviour

| Fails | Result |
|---|---|
| Migration | Transaction rolled back. Nothing deployed. |
| Api deploy or `/api/health/ready` (18 tries, 20 s timeout, 10 s apart) | Previous `/api` files restored from the runner copy and re-checked; Web **not** deployed; job fails. Applied migrations stay (they are additive, so the old Api still runs; otherwise restore the DB backup). |
| Web deploy or `/` check | Previous root files restored (without touching `/api`); the new Api stays live; job fails. |
| Backup (e.g. first deploy, `/api` missing) | Warning only; that part then has no automatic rollback. |

A failed msdeploy sync (after 3 retries; syncs are idempotent, so a retry finishes a partial copy) also deletes a
leftover `app_offline.htm`; the restore removes it too. Two releases never overlap (concurrency group `release`,
no cancelling); if several pushes are queued, GitHub keeps only the newest pending one (it still includes the
skipped pushes' changes: the diff is against the last release tag).

The runner copies are deleted at the end and never uploaded (the server `web.config` holds the Api settings).

### Manual rollback

- App: Actions → Release → Run workflow, "Use workflow from" the previous tag `v…` (it redeploys everything). Key rings are untouched, so sign-ins survive.
- Database: migrations are forward-only; restore the backup taken before the release, or write a new migration.

### Compared with AssetTag

Kept from AssetTag: push to main = release + deploy (version from the csproj, tag and GitHub Release made by the workflow), one connection secret used for migrations and the running Api, app
settings written by CI (nothing on the panel), change detection, signed APK as a GitHub Release asset.
Different on purpose: one site (Web `/`, Api `/api`) instead of two, idempotent SQL via psql instead of
`dotnet ef database update`, secrets written into `web.config` only in the deploy job (never in an uploaded
artifact), health check with automatic rollback, and GitHub Pages in-app updates.

## Local checks

```bash
/tmp/actionlint .github/workflows/*.yml        # github.com/rhysd/actionlint
shellcheck scripts/*.sh .github/scripts/*.sh
.github/scripts/test-scripts.sh                 # detect-changes + android-release + iis-deploy (needs pwsh; PWSH=/path)
```

Action versions are pinned to commit SHAs (comment shows the tag); update them deliberately.
