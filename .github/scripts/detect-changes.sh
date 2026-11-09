#!/usr/bin/env bash
# Decide which parts of NeedleBase a change touches (docs/ci-cd.md, "What builds when").
#
#   .github/scripts/detect-changes.sh <base-sha|''> [head-sha]
#
# Prints (and appends to $GITHUB_OUTPUT when set): core, api, web, mobile, any = true|false.
#   core   Domain + Application (+ their tests)       -> also turns on api, web and mobile (all depend on them)
#   api    Api + Infrastructure (+ Api.Tests), scripts/db-migrate.sh  -> also turns on web (the Web hosts the Api)
#   web    Web (+ Web.Tests, landing page), .github/scripts/iis-deploy/, test-iis-deploy.ps1
#   mobile Mobile + Mobile.Data (+ Mobile.Tests) and the mobile build/version plumbing: scripts/android-release.sh,
#          .github/actions/ (MAUI setup), .github/scripts/update-manifest.sh, .github/scripts/next-version.sh
#   docs-only changes (docs/, *.md, .cursor/) turn nothing on.
#   Shared build settings (*.sln, Directory.Build.*, Directory.Packages.props, global.json, .config/, NuGet.config),
#   an empty/unknown base or a failed diff turn EVERYTHING on (safe default).
#   Other CI plumbing (.github/workflows/, the other .github/scripts) and unknown files rebuild and redeploy the
#   SERVER only: they never ship a new APK (force one with Release > Run workflow > build_android).
set -euo pipefail

base="${1:-}"
head="${2:-HEAD}"
all=false core=false api=false web=false mobile=false

if [[ -z "$base" || "$base" =~ ^0+$ ]]; then
  echo "No base commit: building everything." >&2
  all=true
elif ! files=$(git diff --name-only "$base" "$head" 2>/dev/null); then
  echo "Cannot diff $base..$head (shallow clone or unknown commit): building everything." >&2
  all=true
else
  while IFS= read -r f; do
    [[ -z "$f" ]] && continue
    case "$f" in
      docs/*|*.md|.cursor/*|LICENSE*|.gitignore|.gitattributes|.editorconfig) ;;
      *.sln|Directory.Build.*|Directory.Packages.props|global.json|.config/*|[Nn]u[Gg]et.config) all=true ;;
      src/NeedleBase.Domain/*|src/NeedleBase.Application/*|tests/NeedleBase.Domain.Tests/*|tests/NeedleBase.Application.Tests/*) core=true ;;
      src/NeedleBase.Mobile/*|src/NeedleBase.Mobile.Data/*|tests/NeedleBase.Mobile.Tests/*) mobile=true ;;
      scripts/android-release.sh|.github/scripts/update-manifest.sh|.github/scripts/next-version.sh) mobile=true ;;
      .github/actions/*) mobile=true api=true ;;   # .NET/MAUI setup: used by the APK and the server build
      src/NeedleBase.Api/*|src/NeedleBase.Infrastructure/*|tests/NeedleBase.Api.Tests/*|docker-compose.yml|scripts/db-migrate.sh) api=true ;;
      src/NeedleBase.Web/*|tests/NeedleBase.Web.Tests/*|.github/scripts/iis-deploy/*|.github/scripts/test-iis-deploy.ps1) web=true ;;
      .github/*|scripts/*) api=true ;;
      *) echo "Unclassified path '$f': rebuilding the server (not the app)." >&2; api=true ;;
    esac
  done <<< "$files"
fi

if [[ "$all" == true ]]; then core=true; fi
if [[ "$core" == true ]]; then api=true web=true mobile=true; fi
# One site package: the Web process hosts the Api, so an Api change rebuilds, tests and deploys the Web too.
if [[ "$api" == true ]]; then web=true; fi
any=false
if [[ "$api" == true || "$web" == true || "$mobile" == true ]]; then any=true; fi

out="core=$core
api=$api
web=$web
mobile=$mobile
any=$any"
echo "$out"
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then echo "$out" >> "$GITHUB_OUTPUT"; fi
