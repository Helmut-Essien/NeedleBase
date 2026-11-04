#!/usr/bin/env bash
# Build the signed NeedleBase Android release (docs/android-release.md, docs/ci-cd.md).
#
#   NEEDLEBASE_KEYSTORE_PATH=/secure/place/needlebase-upload.jks \
#   NEEDLEBASE_KEY_ALIAS=needlebase NEEDLEBASE_KEYSTORE_PASS=... NEEDLEBASE_KEY_PASS=... \
#   scripts/android-release.sh --format apk --update-url https://OWNER.github.io/NeedleBase/update.json https://YOUR-API-HOST/ 1.0.0 2
#   scripts/android-release.sh --format aab https://YOUR-API-HOST/ 1.0.0 2
#
# Usage: scripts/android-release.sh [--format apk|aab|both] [--update-url <https update.json>] <api-base-url (https)> <version e.g. 1.0.0> [versionCode]
#   --format apk   universal signed APK for sideloading (adb install / file share)
#   --format aab   Google Play App Bundle (default)
#   --format both  one build, both files, same upload key (no in-app updates)
#   --update-url   APK only: turns on in-app updates from that update.json (GitHub Pages; docs/android-release.md
#                  "Updates"). Without it the APK has no updater. Play bundles never get one (Play policy).
#   versionCode    whole number that must go UP for every build users install (sideload or Play). If left out it
#                  is GITHUB_RUN_NUMBER + NEEDLEBASE_VERSION_CODE_OFFSET (CI); outside CI it must be given.
# Passwords are read by MSBuild from the environment (never passed on the command line or stored in the repo).
# Output: artifacts/android/<version>-<code>-<format>/com.helmutcode.needlebase-Signed.{apk,aab} + SHA256SUMS
# Compile check without a keystore (output must NOT be shipped): NEEDLEBASE_ALLOW_UNSIGNED=true
set -euo pipefail

usage() { echo "Usage: $0 [--format apk|aab|both] [--update-url <url>] <api-base-url> <version> [versionCode]" >&2; exit 2; }

format="aab"
update_url=""
args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --format) [[ $# -ge 2 ]] || usage; format="$2"; shift 2 ;;
    --format=*) format="${1#--format=}"; shift ;;
    --update-url) [[ $# -ge 2 ]] || usage; update_url="$2"; shift 2 ;;
    --update-url=*) update_url="${1#--update-url=}"; shift ;;
    -h|--help) usage ;;
    -*) echo "Unknown option '$1'." >&2; usage ;;
    *) args+=("$1"); shift ;;
  esac
done
[[ ${#args[@]} -eq 2 || ${#args[@]} -eq 3 ]] || usage
case "$format" in apk|aab|both) ;; *) echo "--format must be apk, aab or both (got '$format')." >&2; exit 2 ;; esac

api="${args[0]}"; version="${args[1]}"; code="${args[2]:-}"

if [[ -z "$code" ]]; then
  if [[ -z "${GITHUB_RUN_NUMBER:-}" ]]; then
    echo "Give the versionCode (it must be higher than the last one you installed or uploaded)." >&2
    exit 2
  fi
  offset="${NEEDLEBASE_VERSION_CODE_OFFSET:-0}"
  if [[ ! "$GITHUB_RUN_NUMBER" =~ ^[0-9]{1,9}$ || ! "$offset" =~ ^[0-9]{1,9}$ ]]; then
    echo "GITHUB_RUN_NUMBER and NEEDLEBASE_VERSION_CODE_OFFSET must be whole numbers." >&2
    exit 2
  fi
  code=$((10#$GITHUB_RUN_NUMBER + 10#$offset))
fi

# Fail before a multi-minute build (the csproj guards repeat these checks for direct dotnet publish calls).
if [[ ! "$api" =~ ^https://[^/[:space:]]+(/.*)?$ || "$api" == *".invalid"* ]]; then
  echo "API address must be the real https URL, e.g. https://api.example.com/ (got '$api')." >&2
  exit 2
fi
channel="play" # no in-app updater
if [[ -n "$update_url" ]]; then
  if [[ "$format" != "apk" ]]; then
    echo "--update-url is only for --format apk (a Play bundle must not contain the in-app updater)." >&2
    exit 2
  fi
  if [[ ! "$update_url" =~ ^https://[^/[:space:]]+/[^[:space:]]*\.json$ || "$update_url" == *".invalid"* ]]; then
    echo "--update-url must be the https address of update.json, e.g. https://owner.github.io/NeedleBase/update.json (got '$update_url')." >&2
    exit 2
  fi
  channel="sideload"
fi
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must look like 1.0.0 (got '$version')." >&2
  exit 2
fi
if [[ ! "$code" =~ ^[1-9][0-9]{0,8}$ ]]; then
  echo "versionCode must be a whole number from 1 to 999999999 (got '$code')." >&2
  exit 2
fi
if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK not found on PATH (install the .NET 9 SDK and the maui-android workload)." >&2
  exit 127
fi

cd "$(dirname "$0")/.."
out="artifacts/android/${version}-${code}-${format}"
rm -rf "$out" # never list a stale package from an earlier build of the same version

extra=()
if [[ "${NEEDLEBASE_ALLOW_UNSIGNED:-}" == "true" ]]; then
  extra+=(-p:NeedleBaseAllowUnsignedRelease=true)
  echo "WARNING: unsigned compile check; do not ship this output." >&2
fi

dotnet publish src/NeedleBase.Mobile/NeedleBase.Mobile.csproj -f net9.0-android -c Release \
  -p:NeedleBaseReleaseApiBaseUrl="$api" -p:NeedleBaseVersion="$version" -p:NeedleBaseVersionCode="$code" \
  -p:NeedleBasePackageFormat="$format" -p:NeedleBaseUpdateChannel="$channel" -p:NeedleBaseUpdateManifestUrl="$update_url" \
  ${extra[@]+"${extra[@]}"} -o "$out"

shopt -s nullglob
wanted=()
case "$format" in apk) wanted=(apk) ;; aab) wanted=(aab) ;; both) wanted=(aab apk) ;; esac
found=()
for ext in "${wanted[@]}"; do
  files=("$out"/*-Signed."$ext")
  if [[ ${#files[@]} -eq 0 ]]; then
    echo "Build finished but no signed .$ext was found in $out." >&2
    exit 1
  fi
  found+=("${files[@]}")
done

# Checksums so a sideloaded file can be checked after copying it over (sha256sum -c SHA256SUMS).
if command -v sha256sum >/dev/null 2>&1; then
  (cd "$out" && for f in "${found[@]}"; do sha256sum "$(basename "$f")"; done) > "$out/SHA256SUMS"
fi
printf '%s\n' "${found[@]}"
echo "versionCode=$code"
echo "updateChannel=$channel"
