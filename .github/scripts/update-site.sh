#!/usr/bin/env bash
# Builds the GitHub Pages update site for sideloaded Android installs (docs/android-release.md "Updates").
# Needs bash, jq, sha256sum, curl (previous only). Used by release.yml; tested by test-scripts.sh.
#
#   update-site.sh manifest --apk F --version 1.2.0 --code 42 [--min 0] [--notes FILE] --cert SHA256 [--cert ...] --out update.json
#       Writes update.json (schema 1). apkUrl is relative (apk/<file>), so a custom domain needs no rebuild.
#   update-site.sh previous --base https://owner.github.io/NeedleBase/ --site DIR --code 42
#       Best effort: copies the APK the live site currently offers into DIR/apk/ (verified against its manifest),
#       so phones that read the old, CDN-cached update.json can still download during the switch-over.
#   update-site.sh page --manifest update.json --site DIR --logo PNG
#       Writes DIR/index.html (branded download page), DIR/.nojekyll and DIR/needlebase_logo.png.
set -euo pipefail

die() { echo "update-site: $*" >&2; exit 2; }
need() { command -v "$1" >/dev/null 2>&1 || { echo "update-site: $1 not found" >&2; exit 127; }; }

cmd="${1:-}"; [[ $# -gt 0 ]] && shift
apk="" version="" code="" min="0" notes="" out="" base="" site="" manifest="" logo="" published=""
certs=()
while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 ]] || die "missing value for $1"
  case "$1" in
    --apk) apk="$2" ;; --version) version="$2" ;; --code) code="$2" ;; --min) min="$2" ;;
    --notes) notes="$2" ;; --out) out="$2" ;; --base) base="$2" ;; --site) site="$2" ;;
    --manifest) manifest="$2" ;; --logo) logo="$2" ;; --cert) certs+=("$2") ;; --published) published="$2" ;;
    *) die "unknown option $1" ;;
  esac
  shift 2
done
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
    [[ -z "$out" ]] && die "--out is required"
    need sha256sum
    size=$(wc -c < "$apk" | tr -d ' ')
    (( size > 0 && size <= 400 * 1024 * 1024 )) || die "APK size $size is outside 1 byte..400 MB"
    sha=$(sha256sum "$apk" | cut -d' ' -f1)
    text=""
    if [[ -n "$notes" && -f "$notes" ]]; then text=$(head -c 2000 "$notes" | tr -d '\r'); fi
    published="${published:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}"
    jq -n --arg v "$version" --argjson c "$((10#$code))" --argjson m "$((10#$min))" \
      --arg apk "apk/needlebase-$version-$((10#$code)).apk" --arg sha "$sha" --argjson size "$size" \
      --arg notes "$text" --arg pub "$published" --args '{
        schema: 1, packageName: "com.helmutcode.needlebase", versionName: $v, versionCode: $c,
        minSupportedVersionCode: $m, apkUrl: $apk, sha256: $sha, size: $size,
        signingCertSha256: $ARGS.positional, releaseNotes: $notes, publishedAt: $pub }' "${norm[@]}" > "$out"
    ;;

  previous)
    [[ "$base" =~ ^https?://[^[:space:]]+/$ || "$base" =~ ^file:///[^[:space:]]*/$ ]] || die "--base must be a URL ending in /"
    [[ -n "$site" && "$code" =~ ^[1-9][0-9]{0,8}$ ]] || die "--site and --code are required"
    need curl
    tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
    if ! curl -fsSL --max-time 30 --max-filesize 65536 -o "$tmp/update.json" "${base}update.json?t=$(date +%s)"; then
      echo "No live update.json at ${base} (first publish?); nothing to keep."; exit 0
    fi
    prev_code=$(jq -r '.versionCode // empty' "$tmp/update.json" 2>/dev/null || true)
    prev_apk=$(jq -r '.apkUrl // empty' "$tmp/update.json" 2>/dev/null || true)
    prev_sha=$(jq -r '.sha256 // empty' "$tmp/update.json" 2>/dev/null || true)
    if [[ ! "$prev_code" =~ ^[0-9]+$ || ! "$prev_apk" =~ ^apk/needlebase-[0-9]+\.[0-9]+\.[0-9]+-[0-9]+\.apk$ || ! "$prev_sha" =~ ^[0-9a-f]{64}$ ]]; then
      echo "::warning::Live update.json is not a NeedleBase manifest; the previous APK is not kept."; exit 0
    fi
    if (( 10#$prev_code >= 10#$code )); then
      echo "Live version $prev_code is not older than $code; nothing to keep."; exit 0
    fi
    mkdir -p "$site/apk"
    if curl -fsSL --max-time 600 -o "$tmp/prev.apk" "${base}${prev_apk}" &&
       [[ "$(sha256sum "$tmp/prev.apk" | cut -d' ' -f1)" == "$prev_sha" ]]; then
      mv "$tmp/prev.apk" "$site/$prev_apk"
      echo "Kept previous APK $prev_apk (versionCode $prev_code)."
    else
      echo "::warning::Could not download/verify the previous APK ${base}${prev_apk}; only the new one is published."
    fi
    ;;

  page)
    [[ -f "$manifest" && -n "$site" ]] || die "--manifest and --site are required"
    mkdir -p "$site"
    touch "$site/.nojekyll"
    [[ -n "$logo" && -f "$logo" ]] && cp "$logo" "$site/needlebase_logo.png"
    m() { jq -r "$1" "$manifest"; }
    h() { jq -r "$1 | tostring | @html" "$manifest"; }
    size_mb=$(m '(.size / 1048576 * 10 | round) / 10')
    min_android="Android 5.0 or newer"
    notes_html=$(jq -r '.releaseNotes | select(. != "") | split("\n") | map(@html) | join("<br>")' "$manifest")
    published_html=$(jq -r '.publishedAt // "" | .[0:10] | @html' "$manifest")
    cat > "$site/index.html" <<HTML
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>Download NeedleBase $(h .versionName) for Android</title>
<style>
  :root { --primary:#2E9E36; --primary-dark:#247A2B; --primary-light:#E8F5E9; --charcoal:#202A32; --gray:#5F6B73; --bg:#F4F6F5; }
  * { box-sizing:border-box; }
  body { margin:0; font-family:system-ui,-apple-system,"Segoe UI",Roboto,sans-serif; background:var(--bg); color:var(--charcoal); }
  main { max-width:560px; margin:0 auto; padding:32px 16px 48px; }
  .card { background:#fff; border-radius:16px; padding:24px; box-shadow:0 8px 24px rgba(32,42,50,.08), 0 1px 3px rgba(32,42,50,.06); margin-top:16px; }
  .logo { display:block; margin:0 auto; width:112px; height:auto; }
  h1 { text-align:center; font-size:26px; margin:12px 0 4px; }
  .sub { text-align:center; color:var(--gray); margin:0 0 8px; }
  .btn { display:block; text-align:center; background:var(--primary-dark); color:#fff; text-decoration:none; font-weight:700;
         font-size:18px; padding:14px; border-radius:12px; min-height:48px; }
  .btn:active, .btn:focus-visible { background:#1B5E20; outline:3px solid var(--primary-light); }
  h2 { font-size:18px; margin:0 0 8px; }
  ol { padding-left:20px; margin:0; } li { margin:6px 0; }
  .meta { font-size:13px; color:var(--gray); word-break:break-all; }
  .note { background:var(--primary-light); border-radius:12px; padding:12px 14px; font-size:14px; }
</style>
</head>
<body>
<main>
  <img class="logo" src="needlebase_logo.png" alt="NeedleBase logo" width="112" height="112">
  <h1>NeedleBase for Android</h1>
  <p class="sub">Version $(h .versionName) · ${size_mb} MB · ${min_android}${published_html:+ · ${published_html}}</p>
  <div class="card">
    <a class="btn" href="$(h .apkUrl)" download>Download NeedleBase</a>
    <p class="note">Already installed? Open NeedleBase › More › Check for updates. Updates keep everything saved on your phone.</p>
  </div>
  <div class="card">
    <h2>Install</h2>
    <ol>
      <li>Tap <b>Download NeedleBase</b> and open the file when it finishes.</li>
      <li>If Android asks, allow <b>Install unknown apps</b> for your browser or Files app.</li>
      <li>Tap <b>Install</b>, then open NeedleBase.</li>
    </ol>
  </div>
$( [[ -n "$notes_html" ]] && printf '  <div class="card">\n    <h2>What'"'"'s new</h2>\n    <p>%s</p>\n  </div>\n' "$notes_html" )
  <div class="card meta">
    <p>SHA-256: $(h .sha256)</p>
    <p>Signing certificate SHA-256: $(jq -r '.signingCertSha256 | join(", ") | @html' "$manifest")</p>
    <p>Build $(h .versionCode) · <a href="update.json">update.json</a></p>
  </div>
</main>
</body>
</html>
HTML
    ;;

  *) die "usage: $0 manifest|previous|page [options] (see the header of this file)" ;;
esac
