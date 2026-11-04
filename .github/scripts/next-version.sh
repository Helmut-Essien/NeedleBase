#!/usr/bin/env bash
# Next release version, AssetTag style: the version lives in the code (NeedleBaseVersion in
# src/NeedleBase.Mobile/NeedleBase.Mobile.csproj) and nobody types a tag. If v<that version> is already released,
# the patch number goes up past the highest existing v<major>.<minor>.* tag, so every push gets a fresh version.
# Bump major/minor by editing the csproj. Usage: next-version.sh [csproj]   (run inside the git repo, tags fetched)
set -euo pipefail
csproj="${1:-src/NeedleBase.Mobile/NeedleBase.Mobile.csproj}"
base=$(sed -n 's/.*<NeedleBaseVersion[^>]*>\([0-9]*\.[0-9]*\.[0-9]*\)<\/NeedleBaseVersion>.*/\1/p' "$csproj" | head -n 1)
if [[ ! "$base" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
  echo "No NeedleBaseVersion x.y.z in $csproj." >&2; exit 1
fi
major=${BASH_REMATCH[1]} minor=${BASH_REMATCH[2]} patch=${BASH_REMATCH[3]}
highest=-1
while IFS= read -r t; do
  p=${t##*.}
  [[ "$p" =~ ^[0-9]+$ ]] && (( 10#$p > highest )) && highest=$((10#$p))
done < <(git tag -l "v$major.$minor.*" | grep -E "^v$major\.$minor\.[0-9]+$" || true)
if (( highest >= 10#$patch )); then patch=$((highest + 1)); fi
echo "$major.$minor.$patch"
