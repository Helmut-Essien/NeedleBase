#!/usr/bin/env bash
# Next MOBILE release version (docs/android-release.md, "Versions"). Only called when an APK ships.
#
#   next-version.sh [csproj]        BUMP=patch|minor|major (default patch)   run inside the git repo, tags fetched
#
# last = highest mobile-v<x.y.z> tag (falls back to the old v<x.y.z> tags when there is none yet).
# csproj = NeedleBaseVersion in src/NeedleBase.Mobile/NeedleBase.Mobile.csproj.
#   no last tag              -> csproj
#   csproj > last            -> csproj exactly (edit the csproj to jump to 1.0.0 / 0.2.0)
#   csproj major.minor lower -> error (versions never go backwards)
#   otherwise                -> last bumped by BUMP (patch 0.1.4 -> 0.1.5, minor -> 0.2.0, major -> 1.0.0)
set -euo pipefail
csproj="${1:-src/NeedleBase.Mobile/NeedleBase.Mobile.csproj}"
bump="${BUMP:-patch}"
case "$bump" in patch|minor|major) ;; *) echo "BUMP must be patch, minor or major (got '$bump')." >&2; exit 1 ;; esac

base=$(sed -n 's/.*<NeedleBaseVersion[^>]*>\([0-9]*\.[0-9]*\.[0-9]*\)<\/NeedleBaseVersion>.*/\1/p' "$csproj" | head -n 1)
if [[ ! "$base" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
  echo "No NeedleBaseVersion x.y.z in $csproj." >&2; exit 1
fi
cM=$((10#${BASH_REMATCH[1]})) cm=$((10#${BASH_REMATCH[2]})) cp=$((10#${BASH_REMATCH[3]}))

highest() { # tag prefix -> highest x.y.z among <prefix>x.y.z tags, or nothing
  git tag -l "$1*" | sed -n "s/^$1\([0-9][0-9]*\)\.\([0-9][0-9]*\)\.\([0-9][0-9]*\)$/\1 \2 \3/p" |
    sort -n -k1,1 -k2,2 -k3,3 | tail -n 1
}
last=$(highest mobile-v)
[[ -n "$last" ]] || last=$(highest v)
if [[ -z "$last" ]]; then echo "$cM.$cm.$cp"; exit 0; fi
read -r lM lm lp <<< "$last"
lM=$((10#$lM)) lm=$((10#$lm)) lp=$((10#$lp))

cmp() { # a1 a2 a3 b1 b2 b3 -> -1|0|1
  local i a=("$1" "$2" "$3") b=("$4" "$5" "$6")
  for i in 0 1 2; do
    if (( a[i] > b[i] )); then echo 1; return; fi
    if (( a[i] < b[i] )); then echo -1; return; fi
  done
  echo 0
}
if [[ "$(cmp "$cM" "$cm" 0 "$lM" "$lm" 0)" == -1 ]]; then
  echo "NeedleBaseVersion $cM.$cm.$cp in $csproj is lower than the last release $lM.$lm.$lp; versions never go back. Set it to $lM.$lm.$lp or higher." >&2
  exit 1
fi
if [[ "$(cmp "$cM" "$cm" "$cp" "$lM" "$lm" "$lp")" == 1 ]]; then echo "$cM.$cm.$cp"; exit 0; fi
case "$bump" in
  patch) echo "$lM.$lm.$((lp + 1))" ;;
  minor) echo "$lM.$((lm + 1)).0" ;;
  major) echo "$((lM + 1)).0.0" ;;
esac
