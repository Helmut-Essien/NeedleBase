# CI/CD (GitHub Actions)

| File | What |
|---|---|
| `.github/workflows/ci.yml` | Every PR and push to main/master: detect changes, build + test only the affected parts, Android Debug build, vulnerable-package check. |
| `.github/workflows/release.yml` | Tag `vX.Y.Z` or manual run: CI, signed APK + AAB, Api/Web packages, idempotent migration SQL, GitHub Release with checksums, approval-gated deploy to MonsterASP, optional Google Play upload. |
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

Releases build/deploy only the parts changed since the previous tag: an Api-only tag deploys the Api and applies
migrations, leaves the Web alone and builds no APK. Run the Release workflow with **everything** ticked to force all.

## One-time setup

1. **Environment `production`** (Settings → Environments): add yourself as *Required reviewer*, allow only tags
   `v*` (deployment branches and tags rule; manual deploy runs must then be started from a tag). Put the deploy and database secrets below in this environment, not the
   repository, so only the approved deploy job can read them.
2. **Environment `play-store`** (only when you move to Play): same, with the Play secret.
3. Repository **secrets** and **variables** below. Then set `DEPLOY_ENABLED=true`.
4. Branch protection on main: require "CI result".
5. **GitHub Pages** (in-app updates, [android-release.md](android-release.md#updates-in-app-sideload-builds)):
   Settings → Pages → Source **GitHub Actions**. The `pages` job creates the `github-pages` environment; if it has
   deployment rules, allow tags `v*`. Optional custom domain: set it there, then `UPDATE_BASE_URL` (before the
   first release; the address is baked into the APK).
6. Settings → Actions → General: "Read repository contents" as default workflow permissions (the workflows ask for
   more only where needed: `contents: write` for the Release job, `pages: write` + `id-token: write` for the update site).

### Secrets

| Name | Where | Value |
|---|---|---|
| `ANDROID_KEYSTORE_BASE64` | repo | `base64 -w0 needlebase-upload.jks` (the one upload keystore, forever) |
| `ANDROID_KEYSTORE_PASSWORD` | repo | keystore password |
| `ANDROID_KEY_ALIAS` | repo | key alias, e.g. `needlebase` |
| `ANDROID_KEY_PASSWORD` | repo | key password |
| `PROD_DB_MIGRATION_CONNECTION_STRING` | production | **Schema-owner** login, as a URI `postgresql://owner:PASS@HOST:5432/DB?sslmode=require` or an Npgsql string `Host=..;Database=..;Username=..;Password=..`. Passed to psql as `PG*` variables, never on a command line. |
| `MONSTER_SERVER` | production | Web Deploy server from the panel: bare host, `host:port` or `https://host:8172/msdeploy.axd` |
| `MONSTER_SITE` | production | IIS site name (e.g. `site12345`) |
| `MONSTER_USER` / `MONSTER_PASSWORD` | production | Web Deploy login of the site. No commas or double quotes (msdeploy can't parse them); other special characters are fine. |
| `PLAY_SERVICE_ACCOUNT_JSON` | play-store | Play Console service-account JSON (later) |

App settings (connection string, JWT key, email, WhatsApp, Platform) are **not** CI secrets: they are environment
variables on each MonsterASP site ([production-config.md](production-config.md)); packages contain no secrets.

### Variables

| Name | Default | Meaning |
|---|---|---|
| `SITE_URL` | (required) | `https://your-host/`, the site root. Baked into the Android app (it calls `api/...` under it), checked at `/api/health/ready` after the Api and at `/` after the Web, and written into the packages' `web.config` as Web `Api__BaseUrl` and Api `FrontendBaseUrl` (so they are not needed on the panel). Public, so a variable. |
| `SITE_HTTPS_PENDING` | off | **First deploy only**, before the host has enabled HTTPS: `true` turns the post-deploy health checks into plain-http smoke checks that warn and never roll back (every run prints a warning). The apps stay https-only. Delete it as soon as HTTPS works ([deployment.md](deployment.md)). |
| `MONSTER_ALLOW_UNTRUSTED` | off | `true` adds msdeploy `-allowUntrusted` (only if the Web Deploy endpoint has no valid certificate). |
| `DEPLOY_ENABLED` | off | `true` lets tags/manual runs deploy (always behind the `production` approval). |
| `VERSION_CODE_OFFSET` | 0 | Added to the run number for the Android versionCode (use it to jump above older codes). |
| `MIGRATIONS_MANUAL` | off | `true` = no `PROD_DB_MIGRATION_CONNECTION_STRING`; you apply the release SQL yourself before approving. |
| `PLAY_UPLOAD_ENABLED` | off | `true` = upload the AAB to Play on tags (after the first manual upload). Also builds the AAB. |
| `ANDROID_BUILD_AAB` | off | `true` = build the Play AAB (no updater) even without Play upload. |
| `UPDATE_BASE_URL` | `https://<owner>.github.io/<repo>/` | Address of the update site; the APK reads `<this>update.json`. Set before the first release. |
| `ANDROID_MIN_SUPPORTED_VERSION_CODE` | 0 | Builds below this versionCode must update (required prompt). |
| `UPDATES_PAGES_ENABLED` | on | `false` skips publishing the update site. |
| `PLAY_TRACK` | `internal` | Play track. |
| `PLAY_RELEASE_STATUS` | `completed` | Use `draft` while the app has never been published. |

## Releasing

```bash
git tag v1.2.0 && git push origin v1.2.0
```

1. CI (affected parts) → signed sideload APK with in-app updates (+ Play AAB if enabled; versionCode = run number + offset) and/or Api/Web packages + migration SQL.
2. GitHub Release `v1.2.0` with the files that were built and `SHA256SUMS` (0.x versions are marked pre-release).
3. Deploy waits for your approval. **Back up the database first** (MonsterASP panel), then approve. In order:
   migration SQL (one transaction, stops on first error, nothing deployed if it fails) → copy of the current `/api`
   to the runner → Api into `/api` with `app_offline.htm` → `/api/health/ready` must answer 200 → copy of the
   current root → Web into `/` (the `/api` folder is skipped) → `/` must answer 200. See *Failure behaviour*.
4. Play upload (if enabled) to the internal track.
5. Android changed → GitHub Pages update site (download page, `update.json`, new + previous APK); installed
   sideload apps offer the update. The tag message becomes the release notes.

Manual runs (Actions → Release → Run workflow) take the version, an explicit versionCode, and switches for
GitHub Release, deploy, Play and everything.

### Server packages

Framework-dependent, `UseAppHost=false`, out-of-process hosting. One site ([deployment.md](deployment.md#one-site-web-at--api-at-api)):
Web at the root, Api in the `/api` sub-application. Web Deploy removes stale files (so old DLLs never linger) but
**never touches `App_Data`** (Data Protection key rings), `logs`, `data` or search-engine verification files, and
the Web deploy **never touches `/api`** (msdeploy skip rule). Checked before the approval (job *server*): each
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
no cancelling); if several tags are queued, GitHub keeps only the newest pending one.

The runner copies are deleted at the end and never uploaded (the server `web.config` may hold panel settings).

### Manual rollback

- App: re-run the Release workflow for the previous tag (manual run from that tag, **everything** ticked, deploy on),
  or redeploy its artifacts. Key rings are untouched, so sign-ins survive.
- Database: migrations are forward-only; restore the backup taken before approving, or write a new migration.

### Learned from Platform (and improved)

Platform's `deploy-monsterasp.yml` deploys one app (its Api serves the UI). Adopted: direct msdeploy call with
endpoint normalisation (bare host / `host:port` / URL), comma check, retries, `AppOffline`, masked password output,
`logs` skip; migrations via psql with the connection split into `PG*` variables (URI or Npgsql) and psql discovery.
Improved: two apps in one site (`/api` sub-application, skip rule, non-inheriting `web.config` check), automatic
backup and rollback, readiness check of `/api/health/ready` before the Web, `production` approval and
`DEPLOY_ENABLED`, change detection, SHA-pinned actions, `-allowUntrusted` opt-in instead of always, and app
settings stay in the MonsterASP panel instead of being written into `web.config` from CI secrets.

## Local checks

```bash
/tmp/actionlint .github/workflows/*.yml        # github.com/rhysd/actionlint
shellcheck scripts/*.sh .github/scripts/*.sh
.github/scripts/test-scripts.sh                 # detect-changes + android-release + iis-deploy (needs pwsh; PWSH=/path)
```

Action versions are pinned to commit SHAs (comment shows the tag); update them deliberately.
