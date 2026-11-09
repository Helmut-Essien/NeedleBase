#!/usr/bin/env bash
# update.json for the sideloaded Android app's in-app updater (docs/android-release.md "Updates"), AssetTag style:
# release.yml attaches it to the GitHub Release next to the APK; the Api (GET api/mobile/update) serves the newest
# release's copy to the app. No GitHub Pages. Needs bash, jq, sha256sum. Tested by test-scripts.sh.
#
#   update-manifest.sh manifest --apk F --apk-url https://github.com/O/R/releases/download/mobile-vX/F.apk --version 1.2.0
#                      --code 42 [--min 0] [--notes FILE] --cert SHA256 [--cert ...] --out update.json
#       Writes update.json (schema 1) with the APK's SHA-256, size and signing certificates.
#   update-manifest.sh certs --apk F [--apksigner PATH]    (or: certs --from FILE, parse saved output only)
#       Prints the APK's signing-certificate SHA-256 fingerprints (lowercase hex, one per line, de-duplicated).
#       Reads `apksigner verify --print-certs` in every known wording (build-tools <=36 "Signer #1 certificate
#       SHA-256 digest: ...", 37+ "V3.0 Signer: certificate SHA-256 digest: ...", rotation/v3.1 variants), falling back
#       to `keytool -printcert -jarfile` ("SHA256: AB:CD:..."). Fails (exit 3) if the APK is unsigned or doesn't verify.
set -euo pipefail

die() { echo "update-manifest: $*" >&2; exit 2; }
need() { command -v "$1" >/dev/null 2>&1 || { echo "update-manifest: $1 not found" >&2; exit 127; }; }

cmd="${1:-}"; [[ $# -gt 0 ]] && shift
apk="" version="" code="" min="0" notes="" out="" published="" apksigner="" from="" apkurl=""
certs=()
while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 ]] || die "missing value for $1"
  case "$1" in
    --apk) apk="$2" ;; --version) version="$2" ;; --code) code="$2" ;; --min) min="$2" ;;
    --notes) notes="$2" ;; --out) out="$2" ;;
    --cert) certs+=("$2") ;; --published) published="$2" ;;
    --apksigner) apksigner="$2" ;; --apk-url) apkurl="$2" ;; --from) from="$2" ;;
    *) die "unknown option $1" ;;
  esac
  shift 2
done

# SHA-256 fingerprints from apksigner/keytool text on stdin -> lowercase hex, unique, in first-seen order.
parse_certs() {
  sed -nE 's/.*SHA-?256( digest)?: *([0-9A-Fa-f: ]+).*/\2/Ip' | tr -d ': \r' | tr 'A-F' 'a-f' |
    grep -E '^[0-9a-f]{64}$' | awk '!seen[$0]++' || true
}

if [[ "$cmd" == certs ]]; then
  if [[ -n "$from" ]]; then
    [[ -f "$from" ]] || die "--from file not found"
    found=$(parse_certs < "$from")
  else
    [[ -f "$apk" ]] || die "--apk file not found"
    if [[ -z "$apksigner" ]]; then
      sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
      [[ -n "$sdk" ]] && apksigner=$(find "$sdk/build-tools" -mindepth 2 -maxdepth 2 -name apksigner 2>/dev/null | sort -V | tail -1 || true)
    fi
    [[ -n "$apksigner" && -x "$apksigner" ]] || die "apksigner not found (pass --apksigner or set ANDROID_HOME)"
    if ! text=$("$apksigner" verify --print-certs "$apk" 2>&1); then
      echo "$text" >&2
      echo "update-manifest: $apk is not signed or its signature does not verify" >&2; exit 3
    fi
    found=$(parse_certs <<< "$text")
    if [[ -z "$found" ]] && command -v keytool >/dev/null 2>&1; then
      found=$(keytool -printcert -jarfile "$apk" 2>/dev/null | parse_certs)
    fi
    [[ -n "$found" ]] || echo "$text" >&2
  fi
  [[ -n "$found" ]] || { echo "update-manifest: no signing certificate found" >&2; exit 3; }
  printf '%s\n' "$found"
  exit 0
fi

need jq

case "$cmd" in
  manifest)
    [[ -f "$apk" ]] || die "--apk file not found"
    [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "--version must look like 1.2.3"
    [[ "$code" =~ ^[1-9][0-9]{0,8}$ ]] || die "--code must be 1..999999999"
    [[ "$min" =~ ^[0-9]{1,9}$ ]] || die "--min must be a whole number"
    (( 10#$min <= 10#$code )) || die "--min ($min) must not be above --code ($code)"
    [[ ${#certs[@]} -gt 0 ]] || die "at least one --cert (signing certificate SHA-256) is required"
    norm=()
    for c in "${certs[@]}"; do
      c="${c//:/}"; c="${c,,}"
      [[ "$c" =~ ^[0-9a-f]{64}$ ]] || die "--cert must be a SHA-256 fingerprint (got '$c')"
      norm+=("$c")
    done
    [[ "$apkurl" =~ ^https://[^/[:space:]]+/[^[:space:]]+\.apk$ ]] || die "--apk-url must be the https address of the APK"
    [[ -z "$out" ]] && die "--out is required"
    need sha256sum
    size=$(wc -c < "$apk" | tr -d ' ')
    (( size > 0 && size <= 400 * 1024 * 1024 )) || die "APK size $size is outside 1 byte..400 MB"
    sha=$(sha256sum "$apk" | cut -d' ' -f1)
    text=""
    if [[ -n "$notes" && -f "$notes" ]]; then text=$(head -c 2000 "$notes" | tr -d '\r'); fi
    published="${published:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}"
    jq -n --arg v "$version" --argjson c "$((10#$code))" --argjson m "$((10#$min))" \
      --arg apk "$apkurl" --arg sha "$sha" --argjson size "$size" \
      --arg notes "$text" --arg pub "$published" --args '{
        schema: 1, packageName: "com.helmutcode.needlebase", versionName: $v, versionCode: $c,
        minSupportedVersionCode: $m, apkUrl: $apk, sha256: $sha, size: $size,
        signingCertSha256: $ARGS.positional, releaseNotes: $notes, publishedAt: $pub }' "${norm[@]}" > "$out"
    ;;

  *) die "usage: $0 certs|manifest [options] (see the header of this file)" ;;
esac
