# CI/CD (GitHub Actions)

| File | What |
|---|---|
| `.github/workflows/ci.yml` | Every PR and push to other branches (main runs it inside Release): detect changes, build + test only the affected parts, Android Debug build, vulnerable-package check. |
| `.github/workflows/release.yml` | Every push to main (or manual run): CI, site package + idempotent migration SQL, deploy to MonsterASP; **only when the app changed**: auto version, signed APK + `update.json`, tag `mobile-v<version>` + GitHub pre-release, optional Google Play upload. |
| `.github/scripts/detect-changes.sh` | The one rule set for "what changed" (used by both workflows; runnable locally). |
| `.github/actions/setup-dotnet-maui` | .NET 9 SDK + maui-android workload + NuGet, cached. |
| `.github/scripts/iis-deploy/` | Web Deploy to the one MonsterASP site: `Deploy-Site.ps1` (direct msdeploy: deploy, backup, restore), `WebDeploy.psm1` (package checks, skip rules, endpoint and connection parsing), `Wait-Healthy.ps1`. Tests: `.github/scripts/test-iis-deploy.ps1`, run by `test-scripts.sh`. |

## What builds when

`detect-changes.sh <base>` compares with the PR base or, on main, the previous push (`github.event.before`; further
back to the last successful Release run when that is older, so failed or skipped runs are retried; a new branch,
force push or zero sha builds everything):

| Changed | core (Domain+Application tests) | api | web | mobile (APK) |
|---|---|---|---|---|
| `src/NeedleBase.Domain`, `src/NeedleBase.Application` (+ their tests) | yes | yes | yes | yes |
| `src/NeedleBase.Mobile`, `src/NeedleBase.Mobile.Data`, `tests/NeedleBase.Mobile.Tests` (incl. the csproj/version) | | | | yes |
| `scripts/android-release.sh`, `.github/scripts/update-manifest.sh`, `.github/scripts/next-version.sh` | | | | yes |
| `.github/actions/` (.NET/MAUI setup) | | yes | yes | yes |
| `src/NeedleBase.Api`, `src/NeedleBase.Infrastructure`, `tests/NeedleBase.Api.Tests`, `docker-compose.yml`, `scripts/db-migrate.sh` | | yes | yes | |
| `src/NeedleBase.Web` (landing page included), `tests/NeedleBase.Web.Tests`, `.github/scripts/iis-deploy/`, `test-iis-deploy.ps1` | | | yes | |
| `.github/workflows/`, other `.github/` and `scripts/` files, any unlisted file | | yes | yes | |
| `*.sln`, `Directory.Build.*`, `Directory.Packages.props`, `.config/`, NuGet config, `global.json`, no usable base | yes | yes | yes | yes |
| `docs/`, `*.md`, `.cursor/` only | | | | |

Workflow-only and server-only changes never ship an APK; force one with *Run workflow → build_android*.

Try it: `.github/scripts/detect-changes.sh origin/main`.

**Branch protection:** require only the **"CI result"** check. Skipped jobs count as passed, a failed or cancelled
job fails it, so docs-only PRs can still merge.

Each push to main builds/deploys only what changed: an Api change applies migrations and redeploys the site (the
Web hosts the Api, so it is rebuilt and tested too); a Web-only change redeploys the site without migrations;
neither builds an APK, creates a tag or a release. A manual run rebuilds and redeploys the site (APK only if ticked).

## One-time setup

Same shape as AssetTag's `main.yml`: one database, one secret for it, and nothing to type into the MonsterASP panel.

1. **Environment `production`** (Settings → Environments) holds the deploy and database secrets below (only the
   deploy job can read them). No reviewers needed; add yourself as *Required reviewer* only if you want to approve
   each deploy.
2. Repository **variables** below (at least `SITE_URL`).
3. Optional: branch protection on main requiring "CI result"; environment `play-store` when you move to Play.

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
replaces the site `web.config`, so add them to the workflow step *Write server settings* if you need them
([production-config.md](production-config.md)).

### Variables

| Name | Default | Meaning |
|---|---|---|
| `SITE_URL` | (required) | `https://your-host/`, the site root. Baked into the Android app, used for the health checks and written into the packages as Web `Api__BaseUrl` and Api `FrontendBaseUrl`. |
| `SITE_HTTPS_PENDING` | off | **First deploy only**, before the host has enabled HTTPS: `true` turns the health checks into http smoke checks that warn and never roll back. Delete it once HTTPS works ([deployment.md](deployment.md)). |
| `MONSTER_ALLOW_UNTRUSTED` | off | `true` adds msdeploy `-allowUntrusted` (only if the Web Deploy endpoint has no valid certificate). |
| `VERSION_CODE_OFFSET` | 0 | Added to the run number for the Android versionCode. |
| `ANDROID_MIN_SUPPORTED_VERSION_CODE` | 0 | Builds below this versionCode must update. |
| `PLATFORM_BASE_URL` / `PLATFORM_TOKEN_ISSUER` | unset | Optional, with `PLATFORM_INTEGRATION_KEY`. |
| `PLAY_UPLOAD_ENABLED` | off | `true` = also build the AAB and upload it to Play. |
| `PLAY_TRACK` / `PLAY_RELEASE_STATUS` | `internal` / `completed` | Play upload options. |

No longer used (delete if set): `DEPLOY_ENABLED`, `MIGRATIONS_MANUAL`, `ANDROID_BUILD_AAB`, `UPDATES_PAGES_ENABLED`, `UPDATE_BASE_URL`. GitHub Pages is not used any more (Settings → Pages can be turned off).

## Releasing

Like AssetTag: **push to `main`, that's it.** No `git tag`, no version typing.

```bash
git push origin main
```

- **Server-only push** (Api, Web, Infrastructure, landing page, workflows): CI → migration SQL + site deploy. No
  version bump, no APK, **no tag, no GitHub Release**. The run itself (Actions tab) is the record of the deploy.
- **App push** (Mobile, Mobile.Data, Domain, Application, mobile build scripts, shared build settings): additionally
  a new version, signed APK + `update.json`, tag **`mobile-v<version>`** and a GitHub **pre-release**. When the same
  push also changed the server, the release is made only after the deploy succeeded.
- **Pre-releases:** every CI release is a pre-release. When you have tested it, open it on GitHub → *Edit* → untick
  *Set as a pre-release* → *Update release*. Installed apps are offered pre-releases too (the Api takes the newest
  non-draft release with an `update.json`, pre-release or not; old `v*` releases still count).
- **One run per push:** CI runs inside the Release workflow; `ci.yml` runs only for pull requests and other branches.
  Pushes that touch only `docs/`, `*.md` or `.cursor/` start nothing. Workflow-made tags never start a workflow.

### Versions (app only)

`.github/scripts/next-version.sh`, run only when an APK ships. `last` = highest `mobile-v*` tag (the old `v*` tags
when there is none yet); `csproj` = `NeedleBaseVersion` in `src/NeedleBase.Mobile/NeedleBase.Mobile.csproj`.

| Situation | Next version |
|---|---|
| No tag yet | `csproj` |
| Normal push | `last` + patch (`0.1.4` → `0.1.5`) |
| `csproj` higher than `last` | exactly `csproj` (**minor/major release: edit the csproj**, e.g. `0.2.0` or `1.0.0`, and push) |
| Manual run with *build_android* + *bump* `minor` / `major` | `0.1.4` → `0.2.0` / `1.0.0` (ignored when `csproj` is already higher) |
| `csproj` major.minor lower than `last` | **run fails**: versions never go back; raise the csproj |

versionCode = run number + `VERSION_CODE_OFFSET` (always increases, also across the old `v*` releases). A rerun of
the same run reuses the version already tagged on that commit and updates the existing release (no duplicates).

### Each run

1. CI (affected parts) → site package (Web with the Api inside) + migration SQL, and/or the signed APK.
2. Deploy (site changed): migration SQL (one transaction; nothing deployed if it fails) → server settings written
   into the site package → site into `/` → `/api/health/ready` and `/` must answer 200. A failed step restores
   the previous files (see *Failure behaviour*).
3. App changed: tag `mobile-v<version>` + GitHub pre-release with the APK, `update.json` (APK address, SHA-256,
   size, signing certificate, minimum build), the migration SQL if the Api changed too, and `SHA256SUMS`. Installed
   apps ask their Api (`/api/mobile/update`), which serves the newest release's `update.json`. The commit message
   becomes the release notes. Play upload if enabled.

Manual run (Actions → Release → Run workflow): inputs *build_android* (default off) and *bump*
(patch/minor/major). From `main` it rebuilds and redeploys the site; from an older commit or tag it **rolls the site
back** to that code. The app is built only when *build_android* is ticked (it then gets a new, higher version even
from an old ref, so phones can install it).

### Server packages

Framework-dependent, `UseAppHost=false`, out-of-process hosting. One site, one app ([deployment.md](deployment.md#one-site-one-app-web-at--api-at-api)):
the Web package (artifact `server-site`) contains the Api, which it serves under `/api` in the same process. Web
Deploy removes stale files (so old DLLs, and the old `/api` sub-application files, never linger) but **never touches
`App_Data`** (Data Protection key rings, also `api\App_Data` left by the old layout), `logs`, `data` or
search-engine verification files. Checked before the deploy (job *server*): `web.config` keeps `system.webServer`
inside `<location path="." inheritInChildApplications="false">`, carries the site URLs, and the package has no
`App_Data` or `appsettings.Development.json`.

### Failure behaviour

| Fails | Result |
|---|---|
| Migration | Transaction rolled back. Nothing deployed. |
| Site deploy, `/api/health/ready` (18 tries, 20 s timeout, 10 s apart) or `/` | Previous site files restored from the runner copy and re-checked; job fails. Applied migrations stay (they are additive, so the old version still runs; otherwise restore the DB backup). |
| Backup (e.g. first deploy) | Warning only; no automatic rollback then. |

A failed msdeploy sync (after 3 retries; syncs are idempotent, so a retry finishes a partial copy) also deletes a
leftover `app_offline.htm`; the restore removes it too. Two releases never overlap (concurrency group `release`,
no cancelling); if several pushes are queued, GitHub keeps only the newest pending one (it still includes the
skipped pushes' changes: the diff reaches back to the last successful Release run). A failed run is retried the
same way by the next push.

The runner copies are deleted at the end and never uploaded (the server `web.config` holds the settings).

### Manual rollback

- Site: Actions → Release → Run workflow, "Use workflow from" an older tag/commit (leave *build_android* off). The site is rebuilt from that code and redeployed. Key rings are untouched, so sign-ins survive. The next push that changes the server deploys main again.
- App: phones never install a lower versionCode. To undo a bad app release, revert the code and push (a new, higher version ships), and delete or mark the bad GitHub release as draft so the updater stops offering it.
- Database: migrations are forward-only; restore the backup taken before the release, or write a new migration.

### Compared with AssetTag

Kept from AssetTag: push to main = deploy, and an app release (`mobile-v*` tag, pre-release) only when the app changed, one connection secret used for migrations and the running Api, app
settings written by CI (nothing on the panel), change detection, signed APK as a GitHub Release asset.
Different on purpose: one site and one app (Web `/`, Api `/api` in the same process) instead of two sites, idempotent SQL via psql instead of
`dotnet ef database update`, secrets written into `web.config` only in the deploy job (never in an uploaded
artifact), health check with automatic rollback. In-app updates work like AssetTag (app → own Api → GitHub Releases), plus
a signed `update.json` per release so the app verifies the APK's SHA-256 and signing certificate before installing.

## Local checks

```bash
/tmp/actionlint .github/workflows/*.yml        # github.com/rhysd/actionlint
shellcheck scripts/*.sh .github/scripts/*.sh
.github/scripts/test-scripts.sh                 # detect-changes + next-version + android-release + iis-deploy (needs pwsh; PWSH=/path)
```

Action versions are pinned to commit SHAs (comment shows the tag); update them deliberately.
