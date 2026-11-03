# NeedleBase licensing through Platform

NeedleBase shops are licensed by **Platform** (the license hub). NeedleBase only *reads* Platform: it calls
`POST /api/licenses/validate` and `GET /api/licenses/keys` (JWKS). Only the NeedleBase **server** talks to Platform;
the integration key never reaches browsers or phones.

## How it works

- **New shop (register):** the owner enters a license key. Before anything is created, the server asks Platform
  with the key, the new shop's id (ULID) as `deviceId`, the shop name, and the owner's email.
  - Allowed: a valid license (active, trial or complimentary) whose customer email matches the owner's email.
  - Refused with a per-reason message: invalid key, key used by another shop, expired, suspended, cancelled,
    not active, email doesn't match.
  - Platform unreachable / busy (network, timeout, 429, 5xx, bad token): registration is refused with
    "try again in a few minutes". Nothing is created.
- **Existing shops** (made before licensing): never locked. The owner sees a banner and adds the key once on the
  website's **License** page (`/Settings/License`). Same checks as registration, with the owner's email.
- **Staff** join through invites as before; no key needed.
- **Stored:** the key encrypted with ASP.NET Data Protection, a hint (last 4 characters), and the last answer
  (plan, trial/complimentary, expiry, verified token, next check).
- **Re-checks:** a background worker re-validates due shops (Platform's `refresh_after`, clamped to 30 min to 1 day;
  every 6 h while not valid). Platform being down only backs off (15 min doubling, max 6 h) and never changes a
  shop's status. The email is **not** sent on re-checks.
- **Soft lock:** when Platform says "not valid", the shop gets a warning banner first. It becomes **read-only** only
  after both the token's own grace (`grace_until`) and `Platform:LockGraceDays` (default 7) since the first "not
  valid" answer. Read-only = no new or changed customers, measurements, orders or payments (server returns
  `SUBSCRIPTION_READ_ONLY`); reading, signing in, staff and profile changes, WhatsApp settings and customer opt-outs, and syncing changes made
  earlier still work. A renewal unlocks at the
  next re-check (or immediately when the owner re-enters the key).
- **Phones** get the status with every sync pull, keep it in secure storage, show the banner on Home and refuse new
  work offline while read-only.
- **Rate limits:** register and attach share a strict per-IP limit (`RateLimits:RegisterPermitPer10Minutes`, default 10).

## Configuration (server only)

Set with user-secrets in development or environment variables in production (`Platform__IntegrationKey`, ...).
**Never** put the integration key in appsettings, source control, the web pages or the phone app.

| Key | Meaning |
|-----|---------|
| `Platform:BaseUrl` | Platform's https base URL (http allowed only for localhost). |
| `Platform:IntegrationKey` | Sent as `X-Integration-Key`. Secret. |
| `Platform:ServiceCode` | NeedleBase's product code in Platform (default `NEEDLEBASE`); also the token audience. |
| `Platform:TokenIssuer` | Expected token issuer; empty = not checked. |
| `Platform:RequireLicenseKey` | Default `true`. Development may set `false` to register without a key (unlicensed, never-locked shop). |
| `Platform:LockGraceDays` | Days before read-only after the first "not valid" (default 7). |
| `Platform:TimeoutSeconds` | HTTP timeout, clamped 2-60 (default 10). |
| `Platform:RecheckPollMinutes` | How often the worker looks for due shops (default 15). |

```bash
cd src/NeedleBase.Api
dotnet user-secrets set "Platform:BaseUrl" "https://<platform-host>"
dotnet user-secrets set "Platform:IntegrationKey" "<from Platform admin>"
```

If `BaseUrl`/`IntegrationKey` are missing while `RequireLicenseKey` is true, registration is refused as a server
setup problem (logged).

## What Platform must provide (NeedleBase does not change Platform)

1. **Email check on validate:** accept an optional `email` in the validate request and return
   `emailMatches: true|false` (case-insensitive compare with the license's customer email). Never echo the email
   or the customer's real email back. When no email is sent, return `emailMatches: null`.
   NeedleBase treats a missing `emailMatches` as a mismatch and refuses registration/attach.
2. **One shop per key:** NeedleBase licenses must have `MaxDevices = 1` (the device is the shop id). A second shop
   with the same key must get `device_limit`.
3. **Don't consume a device slot** when the answer is not valid or the email doesn't match; otherwise a typo or a
   stranger's attempt would bind the key to a shop that was never created.
4. **Signed token** on valid answers (ES256, `kid` in JWKS) with `aud` = service code, `device_id`, `license_id`
   (or `sub`), `exp`, and optionally `iss`, `plan`, `trial`, `complimentary`, `license_expires_at`, `refresh_after`,
   `grace_until`. A valid answer without a token that verifies is treated as "couldn't check".
5. Reasons on not-valid answers: `invalid_key`, `device_limit`, `expired`, `suspended`, `revoked`, `not_active`.
   Any other reason (`invalid_integration_key`, `wrong_service`, ...) is treated as NeedleBase's own setup problem:
   "couldn't check", never a lock.
6. 429/503 with `Retry-After` (or `retryAfterSeconds`) when busy.

## Known limits

See `.cursor/skills/needlebase-phases/code-gaps.md`, section "Platform licensing".
