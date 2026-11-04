# Android release (sideload now, Google Play later)

Package id `com.helmutcode.needlebase`. Release builds are a signed **universal APK** (sideloading), a signed
**App Bundle (.aab)** (Google Play), or both from one build, all signed with the same upload key; profiled AOT, target SDK 35
(the .NET 9 Android default; the build stops if it is ever below 35).

## Versions

| Property | Meaning | Rule |
|---|---|---|
| `NeedleBaseVersion` | `versionName` users see | `major.minor.patch`, e.g. `1.0.0` |
| `NeedleBaseVersionCode` | `versionCode` | Whole number that must **go up for every build anyone installs** (Android refuses to update over a higher one; Play refuses a repeat). CI uses the run number + `VERSION_CODE_OFFSET`; by hand, give it explicitly. |

## Upload keystore (once, kept by you)

```bash
keytool -genkeypair -v -keystore ~/secure/needlebase-upload.jks -alias needlebase \
        -keyalg RSA -keysize 4096 -validity 10000
```

Keep the `.jks` and both passwords **outside the repo** (password manager + an offline backup). Losing the upload
key means asking Google to reset it; use Play App Signing so Google holds the app-signing key. Keystore files are
git-ignored.

## Build

```bash
export NEEDLEBASE_KEYSTORE_PATH=~/secure/needlebase-upload.jks
export NEEDLEBASE_KEY_ALIAS=needlebase
read -rs NEEDLEBASE_KEYSTORE_PASS; export NEEDLEBASE_KEYSTORE_PASS
read -rs NEEDLEBASE_KEY_PASS; export NEEDLEBASE_KEY_PASS
# Sideload APK with in-app updates (site ROOT; the app calls api/...):
scripts/android-release.sh --format apk --update-url https://OWNER.github.io/NeedleBase/update.json https://YOUR-SITE/ 1.0.0 2
# Play bundle (no updater, no REQUEST_INSTALL_PACKAGES):
scripts/android-release.sh --format aab https://YOUR-SITE/ 1.0.0 2
```

Output in `artifacts/android/1.0.0-2-apk/` or `-aab/`: `com.helmutcode.needlebase-Signed.apk` / `.aab` and
`SHA256SUMS`. `--format both` still exists but has no updater; `--update-url` with aab/both is refused. Leave out the versionCode only in CI
(it then uses `GITHUB_RUN_NUMBER` + `NEEDLEBASE_VERSION_CODE_OFFSET`). The APK holds armeabi-v7a, arm64-v8a and
x86_64, so it installs on 32-bit budget phones too. Tagged releases build the APK (and the AAB when Play is
enabled) in GitHub Actions and publish the update site ([ci-cd.md](ci-cd.md)).

## Sideloading (for now)

- **Install:** `adb install -r needlebase-1.0.0-2.apk` (USB debugging on), or share the APK (Drive, WhatsApp,
  email) and open it on the phone, allowing "Install unknown apps" for the app that opens it. Check a copied file
  first with `sha256sum -c SHA256SUMS --ignore-missing`.
- **Updates install over the top** and keep the phone's data, *only if* the new APK is signed with the **same
  keystore** and has a **higher versionCode**. Otherwise Android refuses ("App not installed" / signatures don't
  match) and the only way forward is uninstalling, which deletes unsynced local data.
- **Keep the same keystore forever** (password manager + offline backup). Never use the debug key for anything
  handed out.
- Keep versionCodes increasing across both channels: the first Play upload must be higher than any sideloaded
  build. Set the `VERSION_CODE_OFFSET` variable if you ever need to jump ahead of earlier codes.
- Sideloaded phones update themselves from GitHub Pages (next section). The first install comes from the download
  page `https://OWNER.github.io/NeedleBase/` (or `UPDATE_BASE_URL`).

## Updates (in-app, sideload builds)

**How it works.** Each release publishes to GitHub Pages: `index.html` (branded download page), `update.json` and
`apk/needlebase-<version>-<code>.apk` (plus the previous APK, so phones holding a CDN-cached `update.json` can
finish their download). The app (More › *Check for updates*, and automatically when it comes to the foreground,
5 s after start, at most every 6 h):

1. reads `update.json` (15 s timeout, cache-buster, 64 KB limit) and compares **versionCode**;
2. offers an **optional** update ("Later" stops automatic prompts for that version), or a **required** one when
   the installed build is below `minSupportedVersionCode` (no "Later"; still enforced offline from the last copy);
3. asks for **Install unknown apps** first (Android 8+), before downloading anything;
4. downloads with progress into the app cache, **resuming** with HTTP Range after a dropped connection;
5. checks size and **SHA-256**, then package name, versionCode and **signing certificate** (must match the running
   app and the published `signingCertSha256`); a failing file is deleted;
6. gives pending changes up to 20 s to sync, then opens Android's installer (FileProvider). Sync never waits on
   the updater, and unsynced changes stay in the local database across the update.

`update.json` (schema 1): `packageName`, `versionName`, `versionCode`, `minSupportedVersionCode`, `apkUrl`
(relative), `sha256`, `size`, `signingCertSha256` (from `apksigner`), `releaseNotes` (the pushed commit message, max 2000
characters), `publishedAt`.

**Build flag.** `NeedleBaseUpdateChannel`: `sideload` (APK; updater, FileProvider and `REQUEST_INSTALL_PACKAGES`
compiled in) or `play` (no updater; Google Play policy). `NeedleBaseUpdateManifestUrl` is the https address of
`update.json`, baked into the APK, so choose the final address (custom domain) **before the first release**.
Release defaults: apk = sideload, aab/both = play.

**Forcing an update.** Set the `ANDROID_MIN_SUPPORTED_VERSION_CODE` variable to the lowest versionCode that may
keep running (e.g. after an API change), then release.

**Play installs** never get in-app updates, and a Play install can't take a sideloaded APK when Play signs with a
different key (see below).

## Moving to Google Play later

1. Play Console checklist below. **Play App Signing choice matters for sideloaded phones:** Play re-signs apps
   with its *app signing key*. If you let Google generate that key, phones with the sideloaded APK (signed with
   your key) cannot update from Play; they must uninstall once (sync first). To avoid that, when enrolling choose
   to **use your existing key** (export this keystore's key with Google's PEPK tool, as the Console explains);
   Google then signs with the same key and Play updates install over sideloaded builds. Exported `*.pepk` files
   are git-ignored; delete them after upload.
2. Upload the first `.aab` by hand (Play requires the first release of a new app in the Console).
3. Then set `PLAY_UPLOAD_ENABLED=true` and the `PLAY_SERVICE_ACCOUNT_JSON` secret (ci-cd.md); every tag uploads to
   the internal track. Promote in the Console.

The Release build stops with a clear message when:

- the API address is missing, the `.invalid` placeholder, or not https;
- no keystore is set (otherwise .NET would silently sign with the debug key), the file is missing, or the alias/passwords are unset;
- the version or versionCode is malformed;
- the target SDK is below 35;
- `NeedleBaseUpdateChannel` is not sideload/play, sideload is used for an aab/both build, or a sideload APK has
  no https (non-placeholder) `NeedleBaseUpdateManifestUrl`.

Compile check without a keystore (never upload the result):
`NEEDLEBASE_ALLOW_UNSIGNED=true scripts/android-release.sh https://YOUR-SITE/ 0.1.0 1`.
An unknown `--format`/`NeedleBasePackageFormat` (anything but apk, aab, both) also stops the build.

## Play Console (you)

- Developer account, app entry, **Play App Signing** on.
- Privacy policy URL: the live Web `/Privacy` page (fill its placeholders first).
- Data safety: customer names, phone numbers, measurements, orders and payments are stored and synced over https
  to the Api; staff email addresses for sign-in; no ads, no sale of data. `allowBackup=false` (no device backups).
  Permissions: INTERNET, ACCESS_NETWORK_STATE only.
- Account deletion: Play requires a web link where users can ask for their account to be deleted.
- Test on a real device (Journeys 1–5 in phases.md) before the production track.

The guards run on `dotnet publish` (how the bundle is made; the script uses it) and fail in seconds, before
compiling. Plain `dotnet build`/`dotnet test` in Release, Debug builds and IDE design-time builds are not blocked;
add `-p:NeedleBaseReleaseGuards=true` to check them in any build. Never upload output of a non-published build.
