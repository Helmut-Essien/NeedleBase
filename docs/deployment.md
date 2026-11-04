# Deployment (MonsterASP.NET + helmutcode.com)

Settings: [production-config.md](production-config.md). Android: [android-release.md](android-release.md). Automated release/deploy: [ci-cd.md](ci-cd.md) (the steps below are what it does; use them by hand when needed).

## One site: Web at `/`, Api at `/api`

Both apps live in **one** MonsterASP site (one hostname, one certificate, no CORS):

| URL | IIS | Folder | App pool |
|---|---|---|---|
| `https://your-host/` | site root application | `wwwroot` | the site's pool (Web) |
| `https://your-host/api/...` | **application** `/api` | `wwwroot\api` | its **own** pool (Api) |

Panel setup, once:

1. Create the site and bind the hostname + TLS.
2. Create the folder `wwwroot\api`, then add an **application** (not just a virtual directory) with the alias
   `api` pointing at it and its **own application pool** (No Managed Code). Without it `/api/health/ready` returns the
   Web's 404 and the release rolls the Api back.
3. Nothing to set on the panel: the release writes the Api's connection string (from
   `PROD_DB_MIGRATION_CONNECTION_STRING`), JWT key and site URLs into each package's `web.config`
   (see "Precedence" in production-config.md).
4. Enable Web Deploy and put its server, site name and login into the GitHub secrets ([ci-cd.md](ci-cd.md)).

How it fits together: the SDK writes each `web.config` with `<location path="." inheritInChildApplications="false">`,
so the Web's ASP.NET Core handler is not inherited by `/api` (the release checks this). IIS hands the Api requests
with `PathBase=/api`; the Api puts `/api` back into the path (`Configuration/ApiPathBase.cs`) because its routes
already start with `api/`, so the same build works at a host root (development, tests) and under `/api`. Health:
`/api/health/live`, `/api/health/ready` (and `/health/*` inside the Api). The Web deploy skips the `/api` folder,
and every deploy keeps `App_Data`, `logs` and `data`.

## First deploy before HTTPS (free plan)

On the free plan MonsterASP support enables HTTPS only **after** the site is uploaded. The apps stay https-only on
purpose (Secure cookies, https `Api__BaseUrl`, HSTS, https-only OTA URLs); nothing is weakened for this:

1. Keep `SITE_URL=https://needlebase.runasp.net/` (never an http value; the Android app bakes it in).
2. Set the repository variable `SITE_HTTPS_PENDING=true` and push to main. The files are uploaded;
   health checks become plain-http smoke checks that only warn and never roll back.
3. Ask support to enable HTTPS. Until then sign-in does not work (browsers drop Secure cookies on http). Expected.
4. **Delete `SITE_HTTPS_PENDING`** and re-run the deploy: the normal https health checks and automatic rollback return.

## Free-plan limits that matter (MonsterASP help, checked 2026-11-04)

- PostgreSQL is offered on Free (PostgreSQL 18, **Beta**), 1 GB, EU datacenter only. NeedleBase needs PostgreSQL
  (Npgsql, PostgreSQL-specific SQL in migrations); MSSQL/MySQL are not supported by this code.
- 256 MB memory per website, shared low-performance servers: both apps use workstation GC to stay small.
- No AlwaysRunning: an idle site is stopped and the next request cold-starts it (first page can take several
  seconds). Background work is written for this: licence re-checks are stored as due dates in the database and resume
  after a restart; tombstone/audit purges run a couple of minutes after each start; queued email gets one last try
  on shutdown.
- Each database shows a **Connection limit**: keep `Database__MaxPoolSize` (default 20) below it.
- No automatic backups on Free: use the panel's Backups page before each release that has migrations.
- Email only through an external SMTP server (e.g. Gmail). Inactive free services are archived after 6 months.

## Database migrations (every release)

The Api **never migrates itself** outside Development (two instances or a half-finished deploy could race); the
release applies the SQL first. Development keeps auto-migrate + demo seed on start-up.

Each release:

1. Build the idempotent script (needs no database and no secrets):

   ```bash
   scripts/db-migrate.sh script      # -> artifacts/migrations/needlebase-migrate.sql
   ```

   The script checks `__EFMigrationsHistory` and applies only what the target database is missing, inside one
   transaction for the whole script: with `ON_ERROR_STOP=1` a failure rolls everything back. Running it twice, or
   on an up-to-date database, changes nothing. It contains no `CREATE INDEX CONCURRENTLY` (which can't run in a
   transaction); keep it that way or apply such a migration separately.
2. **Back up the database first** (MonsterASP panel backup, or `pg_dump -Fc`).
3. Apply it with the schema-owner login:

   ```bash
   psql "host=... port=5432 dbname=... user=<owner> sslmode=require" -v ON_ERROR_STOP=1 \
        -f artifacts/migrations/needlebase-migrate.sql
   ```

   or paste it into the hosting panel's SQL tool. Alternative: `scripts/db-migrate.sh bundle win-x64` makes a
   self-contained `efbundle.exe` run as `efbundle.exe --connection "<owner connection string>"`.
4. Deploy the Api, then the Web. Check `https://your-host/api/health/ready` and the start-up log (a **Critical** "migration(s) behind"
   line means step 3 was skipped).

Before a release whose migrations drop or rewrite columns/tables (search the script for `DROP` and `UPDATE`),
test the script on a restored copy of production first. `scripts/db-migrate.sh list` shows the migrations in the code.

## Publish

```bash
dotnet publish src/NeedleBase.Api -c Release -o artifacts/publish/api
dotnet publish src/NeedleBase.Web -c Release -o artifacts/publish/web
```

Upload `api` into `wwwroot\api` and `web` into `wwwroot` **without deleting `wwwroot\api`** (Web Deploy or FTP; by
hand, with `Deploy-Site.ps1 -AppPath api` and `-ProtectSubApp api`, see ci-cd.md). Keep `App_Data` on the server
(see "Data Protection keys" in production-config.md). Publish profiles (`*.pubxml`, `*.publishSettings`) contain
credentials and are git-ignored.

## Checklist

- [ ] DNS + TLS for the site hostname on helmutcode.com (https only; HSTS is on in Production).
- [ ] `/api` is an IIS application with its own app pool.
- [ ] Start-up log shows no configuration errors (settings come from the release).
- [ ] Database backup before releases with migrations; migration script applied.
- [ ] Data Protection key folders writable, outside/kept on deploy, backed up.
- [ ] `ForwardedHeaders__KnownProxies`/`KnownNetworks` set to the host's proxy addresses.
- [ ] Uptime monitor on `https://your-host/api/health/ready`.
