# Production configuration

Every setting a real NeedleBase deployment (MonsterASP.NET + helmutcode.com) needs. Nothing secret goes in the
repo: `appsettings.json` ships empty values, Development uses user-secrets, production uses **environment
variables** on the server.

Environment variable names use `__` (two underscores) for `:`. Example: `JwtSettings:SecurityKey` →
`JwtSettings__SecurityKey`.

## Start-up check

Outside Development (and the `Testing` environment used by integration tests) the Api validates its settings
**before it starts** (`src/NeedleBase.Api/Configuration/ProductionConfigValidator.cs`). If anything critical is
missing or wrong it stops with one message listing every problem (setting names only, never values). Optional
features that are simply off (email, WhatsApp, token issuer) are logged as warnings at start-up.

In the single-site layout ([deployment.md](deployment.md#one-site-web-at--api-at-api)) `Api:BaseUrl` is the **site root**
(`https://your-host/`): the Web's calls are relative `api/...` paths. The Web refuses to start without an https `Api:BaseUrl` outside Development, and with a bad
`ForwardedHeaders:*` or relative `DataProtection:KeysPath`.

## Api (`src/NeedleBase.Api`)

| Setting (env var) | Required | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` (IIS default when unset). Never `Development` on the server: Development migrates, seeds demo data and shows error details. |
| `ConnectionStrings__DefaultConnection` | **yes** | PostgreSQL, e.g. `Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require`. Use a login that is **not** the schema owner for the running app (owner only for migrations; see deployment.md). On MonsterASP use the **Local access** string for the site. Checked at start-up (an invalid string stops the Api). |
| `Database__MaxPoolSize` | no | Default 20. Connection-pool cap used when the connection string has no `Maximum Pool Size` (Npgsql's own default is 100). Keep it below the database's **Connection limit** (MonsterASP: database Overview page). |
| `JwtSettings__SecurityKey` | **yes** | ≥ 32 characters, random (e.g. `openssl rand -base64 48`). The Development key is refused. Changing it signs everyone out. |
| `JwtSettings__Issuer` / `JwtSettings__Audience` | no | Defaults `NeedleBaseApi` / `NeedleBaseClient`. |
| `JwtSettings__AccessTokenExpirationMinutes` / `RefreshTokenExpirationDays` | no | Defaults 60 / 7. |
| `FrontendBaseUrl` | **yes**, automatic | https address of the Web app (links in emails). The release writes it from `SITE_URL` into the Api package's `web.config`; set it yourself only for a manual deploy. |
| `Platform__BaseUrl` | **yes**\* | https Platform licensing address. |
| `Platform__IntegrationKey` | **yes**\* | Secret from Platform for the `NEEDLEBASE` service. |
| `Platform__TokenIssuer` | recommended | Expected license-token issuer; empty = not checked (warning). |
| `Platform__RequireLicenseKey` | no | `true`/`false` (anything else is a start-up error). Default `true`. `false` = shops register without a key and are never locked (warning). \*Platform settings are only required while this is `true`. |
| `Platform__ServiceCode`, `LockGraceDays`, `TimeoutSeconds`, `RecheckPollMinutes` | no | See [platform-licensing.md](platform-licensing.md). |
| `Email__Host`, `Email__Port`, `Email__Username`, `Email__Password` | optional, all or none | SMTP. Off when none set (invite/reset codes are shared by hand, Forgot password hidden). Half-set = start-up error. Gmail caps ~500 mails/day; use a real provider for launch. |
| `Email__FromAddress`, `Email__FromName`, `Email__AppBaseUrl` | no | From defaults to the username; links default to `FrontendBaseUrl`. |
| `WhatsApp__AccessToken`, `WhatsApp__PhoneNumberId` | optional, both or neither | Meta Cloud API. With them, at least one `WhatsApp__Templates__<Status>` is required. |
| `WhatsApp__AppSecret`, `WhatsApp__VerifyToken` | with WhatsApp | Webhook (delivery ticks, STOP). Missing = warning. See [whatsapp-setup.md](whatsapp-setup.md). |
| `WhatsApp__BusinessAccountId`, `ApiVersion`, `TemplateLanguage`, `DelaySeconds`, `MaxPerBusinessPerHour` | no | Defaults in `WhatsAppOptions`. |
| `ForwardedHeaders__KnownProxies` | if behind a proxy | Comma-separated proxy IPs (full form: `10.0.0.5`, not `10.5`, no ports) whose `X-Forwarded-For/-Proto` are trusted (loopback always is). Ask MonsterASP. Without it, every user looks like the proxy IP to the rate limits and login throttle. If the Api log says "The Web front end reaches the Api from X", add X here (the Web's calls re-enter through the host's network). |
| `ForwardedHeaders__KnownNetworks` | if behind a proxy | Comma-separated CIDR ranges, e.g. `10.0.0.0/8`. `/0` (trust everyone) is refused: anyone could fake their IP and dodge rate limits. |
| `DataProtection__KeysPath` | recommended | Absolute folder for the Api key ring (encrypts stored license keys). Default `<site>/App_Data/Keys/Api`. |
| `RateLimits__AuthPermitPerMinute` / `RegisterPermitPer10Minutes` / `RefreshPermitPerMinute` | no | Defaults 30 / 10 / 300 per IP. |

## Web (`src/NeedleBase.Web`)

| Setting (env var) | Required | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | yes | `Production`. |
| `Api__BaseUrl` | **yes**, automatic | The **site root** (the Web calls `api/...` under it). The release writes it from `SITE_URL` into the Web package's `web.config`; set it yourself only for a manual deploy. |
| `ForwardedHeaders__KnownProxies` / `KnownNetworks` | if behind a proxy | Same format as the Api. Needed so HTTPS redirect / Secure cookies see the real scheme. |
| `DataProtection__KeysPath` | recommended | Absolute folder for the cookie/antiforgery key ring. Default `<site>/App_Data/Keys`. |

## Data Protection keys: persist and back up

Both apps keep an encryption key ring on disk:

- **Api** (`App_Data/Keys/Api` or `DataProtection__KeysPath`): encrypts each shop's license key. If lost, stored
  keys can't be read and those shops show "needs re-attach" until the owner enters the key again (never locked).
- **Web** (`App_Data/Keys` or `DataProtection__KeysPath`): signs the sign-in and antiforgery cookies. If lost,
  everyone is signed out once.

Rules:

1. The IIS app-pool identity must be able to **write** the folder (MonsterASP: check folder permissions in the
   file manager).
2. The folder must **survive deploys**. Prefer a `DataProtection__KeysPath` outside the published site folder; if
   it is inside (`App_Data`), make sure Web Deploy / FTP publish does not delete extra files (Web Deploy: "Leave
   extra files on destination" / exclude `App_Data`).
3. **Back it up** (download the folder) after the first start and after each 90-day key rotation. The files are
   secrets: keep backups private, never in git (`App_Data/` is git-ignored).
4. Each app has its own folder; do not share one folder between the Api and Web.

## Precedence: web.config vs panel

- The release writes only two **non-secret** values, from the `SITE_URL` repository variable (normalised to one
  trailing `/`, must be an `https://host/` site root): Web `Api__BaseUrl` and Api `FrontendBaseUrl`. They go into
  the package's `web.config` as `<aspNetCore><environmentVariables>`, inside the
  `<location path="." inheritInChildApplications="false">` so the Web's values never leak into `/api`. Only these
  two names are allowed; secrets are never written. The package check fails the release if either is missing or wrong.
- The ASP.NET Core Module sets `web.config` environment variables on the app process, so a `web.config` value
  **wins** over the same name set on the panel/app pool, and environment variables win over `appsettings.json`.
- So the panel values for these two keys are no longer needed (delete them to avoid confusion). To change the
  address, change `SITE_URL` and re-run the release. Everything else (connection string, JWT key, Platform key, ...)
  stays on the panel.

## Setting variables on MonsterASP (IIS)

1. Control panel → your site → look for **Environment variables** / **Application settings** and add each
   variable there (name with `__`).
2. If the panel has no such screen, edit the **deployed** `web.config` on the server (not in the repo) and add
   them inside `<aspNetCore ...>`:

   ```xml
   <environmentVariables>
     <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
     <environmentVariable name="ConnectionStrings__DefaultConnection" value="..." />
   </environmentVariables>
   ```

   A re-publish overwrites `web.config`; keep a private copy of these lines. Never put secrets in the repo.
3. Recycle the app pool, then open `/health/ready` (should say `Healthy`) and read the start-up log for
   configuration warnings or the error list.

## Health and monitoring

- `/health/live`: the process answers. `/health/ready`: the database answers. Point an uptime monitor at
  `/health/ready` on the Api.
- At start-up in Production the Api logs **Critical** if the database is behind the code's migrations
  (see [deployment.md](deployment.md)).
