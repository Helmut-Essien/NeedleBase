#!/usr/bin/env bash
# Self-tests for the CI/CD shell scripts (run by ci.yml; locally: .github/scripts/test-scripts.sh).
# Needs only bash + git; builds nothing.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
detect="$root/.github/scripts/detect-changes.sh"
release="$root/scripts/android-release.sh"
pass=0 fail=0

check() { # name expected actual
  if [[ "$2" == "$3" ]]; then pass=$((pass + 1)); else fail=$((fail + 1)); echo "FAIL: $1"; echo "  want: $2"; echo "  got:  $3"; fi
}

# detect-changes: commit the given files in a throw-away repo and diff against the parent.
changes() {
  local d base
  d=$(mktemp -d)
  (
    cd "$d"
    git init -q
    git -c user.name=t -c user.email=t@t commit -q --allow-empty -m base
    base=$(git rev-parse HEAD)
    for f in "$@"; do mkdir -p "$(dirname "$f")"; echo x > "$f"; done
    git add -A
    git -c user.name=t -c user.email=t@t commit -q -m change
    GITHUB_OUTPUT='' "$detect" "$base" 2>/dev/null | tr '\n' ' '
  )
  rm -rf "$d"
}

none="core=false api=false web=false mobile=false any=false "
all="core=true api=true web=true mobile=true any=true "
check "docs only"         "$none" "$(changes docs/a.md README.md .cursor/x/y.md)"
check "web"               "core=false api=false web=true mobile=false any=true " "$(changes src/NeedleBase.Web/Pages/x.cshtml)"
check "web tests"         "core=false api=false web=true mobile=false any=true " "$(changes tests/NeedleBase.Web.Tests/x.cs)"
check "infrastructure"    "core=false api=true web=true mobile=false any=true " "$(changes src/NeedleBase.Infrastructure/x.cs)"
check "api"               "core=false api=true web=true mobile=false any=true " "$(changes src/NeedleBase.Api/x.cs)"
check "mobile data"       "core=false api=false web=false mobile=true any=true " "$(changes src/NeedleBase.Mobile.Data/x.cs)"
check "api + mobile"      "core=false api=true web=true mobile=true any=true " "$(changes src/NeedleBase.Api/x.cs src/NeedleBase.Mobile/y.cs)"
check "domain -> all"     "$all" "$(changes src/NeedleBase.Domain/x.cs)"
check "application -> all" "$all" "$(changes src/NeedleBase.Application/x.cs)"
check "workflow -> all"   "$all" "$(changes .github/workflows/ci.yml)"
check "script -> all"     "$all" "$(changes scripts/db-migrate.sh)"
check "sln -> all"        "$all" "$(changes NeedleBase.sln)"
check "unknown -> all"    "$all" "$(changes something/new.txt)"
check "no base -> all"    "$all" "$(GITHUB_OUTPUT='' "$detect" '' 2>/dev/null | tr '\n' ' ')"
check "zero base -> all"  "$all" "$(GITHUB_OUTPUT='' "$detect" 0000000000000000000000000000000000000000 2>/dev/null | tr '\n' ' ')"
check "bad base -> all"   "$all" "$(cd "$root" && GITHUB_OUTPUT='' "$detect" ffffffffffffffffffffffffffffffffffffffff 2>/dev/null | tr '\n' ' ')"

# android-release.sh argument checks (all fail before dotnet is called; exit code 2).
code() { local rc=0; env -u GITHUB_RUN_NUMBER "$@" >/dev/null 2>&1 || rc=$?; echo "$rc"; }
check "bad format"        2 "$(code "$release" --format zip https://a.example/ 1.0.0 2)"
check "missing format"    2 "$(code "$release" https://a.example/ 1.0.0 2 --format)"
check "unknown option"    2 "$(code "$release" --apk https://a.example/ 1.0.0 2)"
check "http url"          2 "$(code "$release" --format apk http://a.example/ 1.0.0 2)"
check "placeholder url"   2 "$(code "$release" https://api.needlebase-placeholder.invalid/ 1.0.0 2)"
check "bad version"       2 "$(code "$release" https://a.example/ 1.0 2)"
check "zero code"         2 "$(code "$release" https://a.example/ 1.0.0 0)"
check "10-digit code"     2 "$(code "$release" https://a.example/ 1.0.0 1234567890)"
check "no code, no CI"    2 "$(code "$release" https://a.example/ 1.0.0)"
check "too many args"     2 "$(code "$release" https://a.example/ 1.0.0 2 3)"
check "bad run number"    2 "$(code env GITHUB_RUN_NUMBER=abc "$release" https://a.example/ 1.0.0)"
check "bad offset"        2 "$(code env GITHUB_RUN_NUMBER=5 NEEDLEBASE_VERSION_CODE_OFFSET=-1 "$release" https://a.example/ 1.0.0)"
u=https://a.example/api/mobile/update
check "update-url + aab"  2 "$(code "$release" --format aab --update-url "$u" https://a.example/ 1.0.0 2)"
check "update-url + both" 2 "$(code "$release" --format both --update-url "$u" https://a.example/ 1.0.0 2)"
check "update-url default aab" 2 "$(code "$release" --update-url "$u" https://a.example/ 1.0.0 2)"
check "update-url http"   2 "$(code "$release" --format apk --update-url http://o.github.io/update.json https://a.example/ 1.0.0 2)"
check "update-url no path"  2 "$(code "$release" --format apk --update-url https://o.example/ https://a.example/ 1.0.0 2)"
check "update-url + no-updates" 2 "$(code "$release" --format apk --no-updates --update-url "$u" https://a.example/ 1.0.0 2)"
check "update-url invalid" 2 "$(code "$release" --format apk --update-url https://x.invalid/update.json https://a.example/ 1.0.0 2)"
check "update-url missing" 2 "$(code "$release" --format apk https://a.example/ 1.0.0 2 --update-url)"
# A valid sideload command gets past every check; a PATH holding only bash (no dotnet) stops it right after (127).
nodotnet=$(mktemp -d)
ln -s "$(command -v bash)" "$nodotnet/bash"
check "valid sideload args" 127 "$(code env PATH="$nodotnet" bash "$release" --format apk --update-url="$u" https://a.example/ 1.0.0 2)"
rm -rf "$nodotnet"
check "run+offset > max"  2 "$(code env GITHUB_RUN_NUMBER=999999999 NEEDLEBASE_VERSION_CODE_OFFSET=1 "$release" https://a.example/ 1.0.0)"

# update-manifest.sh (update.json attached to the GitHub Release; certs from the APK). Needs jq (on GitHub's runners).
site="$root/.github/scripts/update-manifest.sh"
# certs: apksigner/keytool output (real wordings captured from build-tools 35 and 37 on a throw-away keystore).
h="e8537298a34ed82ceeea3ebf4644bc2d9072c8a3be6259128220f1271a396eab"
h2="$(printf '0%.0s' {1..63})1"
ct=$(mktemp -d)
printf 'Signer #1 certificate DN: CN=T\nSigner #1 certificate SHA-256 digest: %s\nSigner #1 certificate SHA-1 digest: 004713538334e732664a02977148878fd84a5115\n' "$h" > "$ct/bt35.txt"
printf 'V3.0 Signer: certificate DN: CN=T\nV3.0 Signer: certificate SHA-256 digest: %s\nV3.0 Signer: certificate MD5 digest: 1611c6accf883aa3c56d348e3d2ad7de\n' "$h" > "$ct/bt37.txt"
printf 'WARNING: META-INF/x not protected\nSigner (minSdkVersion=24, maxSdkVersion=32) certificate SHA-256 digest: %s\nSigner (minSdkVersion=33, maxSdkVersion=2147483647) certificate SHA-256 digest: %s\nV3.1 Signer: certificate SHA-256 digest: %s\r\n' "$h" "$h2" "$h2" > "$ct/rot.txt"
printf 'Certificate fingerprints:\n\t SHA1: 00:47\n\t SHA256: %s\n' "$(echo "$h" | tr a-f A-F | sed 's/../&:/g; s/:$//')" > "$ct/keytool.txt"
printf 'Verifies\nVerified using v2 scheme: true\n' > "$ct/none.txt"
check "certs build-tools 35" "$h" "$(bash "$site" certs --from "$ct/bt35.txt")"
check "certs build-tools 37" "$h" "$(bash "$site" certs --from "$ct/bt37.txt")"
check "certs rotation unique" "$h $h2" "$(bash "$site" certs --from "$ct/rot.txt" | tr '\n' ' ' | sed 's/ $//')"
check "certs keytool"        "$h" "$(bash "$site" certs --from "$ct/keytool.txt")"
check "certs none"           3 "$(code bash "$site" certs --from "$ct/none.txt")"
printf '#!/bin/sh\necho "DOES NOT VERIFY"; exit 1\n' > "$ct/apksigner"; chmod +x "$ct/apksigner"; printf 'x' > "$ct/u.apk"
check "certs unsigned apk"   3 "$(code bash "$site" certs --apk "$ct/u.apk" --apksigner "$ct/apksigner")"
printf '#!/bin/sh\ncat "%s"\n' "$ct/bt37.txt" > "$ct/apksigner"
check "certs from apksigner" "$h" "$(bash "$site" certs --apk "$ct/u.apk" --apksigner "$ct/apksigner")"
rm -rf "$ct"
if command -v jq >/dev/null 2>&1; then
  t=$(mktemp -d)
  u="https://github.com/o/r/releases/download/v1.2.0/needlebase-1.2.0-42.apk"
  printf 'fake apk bytes' > "$t/a.apk"
  printf 'Faster sync\n<script>alert(1)</script> & more' > "$t/notes.txt"
  cert="AA:BB:$(printf 'c%.0s' {1..60})"
  check "manifest ok" 0 "$(code bash "$site" manifest --apk-url "$u" --apk "$t/a.apk" --version 1.2.0 --code 42 --min 40 --notes "$t/notes.txt" --cert "$cert" --published 2026-11-04T10:00:00Z --out "$t/update.json")"
  check "manifest fields" "1 com.helmutcode.needlebase 1.2.0 42 40 https://github.com/o/r/releases/download/v1.2.0/needlebase-1.2.0-42.apk 14" \
    "$(jq -r '[.schema,.packageName,.versionName,.versionCode,.minSupportedVersionCode,.apkUrl,.size]|join(" ")' "$t/update.json")"
  check "manifest sha" "$(sha256sum "$t/a.apk" | cut -d' ' -f1)" "$(jq -r .sha256 "$t/update.json")"
  check "manifest cert normalised" "aabb$(printf 'c%.0s' {1..60})" "$(jq -r '.signingCertSha256[0]' "$t/update.json")"
  check "manifest notes" "Faster sync" "$(jq -r '.releaseNotes' "$t/update.json" | head -1)"
  check "manifest min > code" 2 "$(code bash "$site" manifest --apk-url "$u" --apk "$t/a.apk" --version 1.2.0 --code 42 --min 43 --cert "$cert" --out "$t/x.json")"
  check "manifest no cert"    2 "$(code bash "$site" manifest --apk-url "$u" --apk "$t/a.apk" --version 1.2.0 --code 42 --out "$t/x.json")"
  check "manifest bad cert"   2 "$(code bash "$site" manifest --apk-url "$u" --apk "$t/a.apk" --version 1.2.0 --code 42 --cert abc --out "$t/x.json")"
  check "manifest bad version" 2 "$(code bash "$site" manifest --apk-url "$u" --apk "$t/a.apk" --version 1.2 --code 42 --cert "$cert" --out "$t/x.json")"
  check "manifest no apk"     2 "$(code bash "$site" manifest --apk-url "$u" --apk "$t/none.apk" --version 1.2.0 --code 42 --cert "$cert" --out "$t/x.json")"
  check "unknown command"     2 "$(code bash "$site" publish)"
  check "manifest no apk url"  2 "$(code bash "$site" manifest --apk "$t/a.apk" --version 1.2.0 --code 42 --cert "$cert" --out "$t/x.json")"
  check "manifest http apk url" 2 "$(code bash "$site" manifest --apk-url "http://x/a.apk" --apk "$t/a.apk" --version 1.2.0 --code 42 --cert "$cert" --out "$t/x.json")"
  rm -rf "$t"
elif [[ -n "${CI:-}" ]]; then
  echo "FAIL: jq not found; the update-manifest tests could not run"; fail=$((fail + 1))
else
  echo "WARN: jq not found; skipped the update-manifest tests"
fi

# next-version: version from the csproj, patch raised past existing tags.
nextver() { # csproj-version tags...
  local d; d=$(mktemp -d)
  (
    cd "$d"; git init -q
    printf '<Project><NeedleBaseVersion Condition="x">%s</NeedleBaseVersion></Project>\n' "$1" > app.csproj
    git add -A; git -c user.name=t -c user.email=t@t commit -q -m c
    shift; for t in "$@"; do git tag "$t"; done
    "$root/.github/scripts/next-version.sh" app.csproj 2>&1
  )
  rm -rf "$d"
}
check "version: first release"    "0.1.0" "$(nextver 0.1.0)"
check "version: patch bumped"     "0.1.3" "$(nextver 0.1.0 v0.1.0 v0.1.2 v0.1.1)"
check "version: 10 after 9"       "0.1.10" "$(nextver 0.1.0 v0.1.9)"
check "version: new minor"        "0.2.0" "$(nextver 0.2.0 v0.1.7)"
check "version: csproj ahead"     "0.1.5" "$(nextver 0.1.5 v0.1.2)"
check "version: other tags ignored" "1.0.0" "$(nextver 1.0.0 v1.0.0-rc v10.0.4 mobile-v1.0.0)"
echo "script tests: $pass passed, $fail failed"

# --- iis-deploy PowerShell helpers (single-site Web + /api deploy). pwsh ships on GitHub's ubuntu runners;
# PWSH lets a local run point at another install. Missing pwsh fails in CI and is a warning locally.
pwsh_bin="${PWSH:-pwsh}"
if command -v "$pwsh_bin" >/dev/null 2>&1; then
  "$pwsh_bin" -NoProfile -NonInteractive -File "$(dirname "$0")/test-iis-deploy.ps1" || fail=$((fail + 1))
elif [[ -n "${CI:-}" ]]; then
  echo "FAIL: pwsh not found; the iis-deploy tests could not run"
  fail=$((fail + 1))
else
  echo "WARN: pwsh not found; skipped the iis-deploy tests (set PWSH=/path/to/pwsh)"
fi

[[ $fail -eq 0 ]]
