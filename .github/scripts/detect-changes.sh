#!/usr/bin/env bash
# Decide which parts of NeedleBase a change touches (docs/ci-cd.md, "What builds when").
#
#   .github/scripts/detect-changes.sh <base-sha|''> [head-sha]
#
# Prints (and appends to $GITHUB_OUTPUT when set): core, api, web, mobile, any = true|false.
#   core   Domain + Application (+ their tests)       -> also turns on api, web and mobile (all depend on them)
#   api    Api + Infrastructure (+ Api.Tests)             -> also turns on web (the Web hosts the Api on the site)
#   web    Web (+ Web.Tests)
#   mobile Mobile + Mobile.Data (+ Mobile.Tests)
#   docs-only changes (docs/, *.md, .cursor/) turn nothing on.
#   CI/build plumbing (.github/, scripts/, *.sln, Directory.Build.*, .config/, NuGet.config, global.json),
#   an empty/unknown base, a failed diff or any file not listed below turns EVERYTHING on (safe default).
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
      .github/*|scripts/*|*.sln|Directory.Build.*|Directory.Packages.props|global.json|.config/*|[Nn]u[Gg]et.config) all=true ;;
      src/NeedleBase.Domain/*|src/NeedleBase.Application/*|tests/NeedleBase.Domain.Tests/*|tests/NeedleBase.Application.Tests/*) core=true ;;
      src/NeedleBase.Api/*|src/NeedleBase.Infrastructure/*|tests/NeedleBase.Api.Tests/*|docker-compose.yml) api=true ;;
      src/NeedleBase.Web/*|tests/NeedleBase.Web.Tests/*) web=true ;;
      src/NeedleBase.Mobile/*|src/NeedleBase.Mobile.Data/*|tests/NeedleBase.Mobile.Tests/*) mobile=true ;;
      *) echo "Unclassified path '$f': building everything." >&2; all=true ;;
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
