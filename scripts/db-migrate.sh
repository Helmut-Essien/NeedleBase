#!/usr/bin/env bash
# Production database migrations for NeedleBase (docs/deployment.md).
#
# The Api never migrates itself outside Development. Each release ships an IDEMPOTENT SQL script: it checks
# __EFMigrationsHistory and applies only the migrations the target database is missing. The whole script runs in
# ONE transaction (START TRANSACTION ... COMMIT), so with ON_ERROR_STOP a failure leaves the database unchanged,
# and running it twice (or on an up-to-date database) changes nothing.
#
#   scripts/db-migrate.sh script            -> artifacts/migrations/needlebase-migrate.sql (default)
#   scripts/db-migrate.sh bundle            -> artifacts/migrations/efbundle (self-contained runner, linux-x64)
#   scripts/db-migrate.sh bundle win-x64    -> artifacts/migrations/efbundle.exe
#   scripts/db-migrate.sh list              -> migrations known to the code
#
# Apply the script with psql (or the MonsterASP database panel's SQL tool):
#   psql "host=... port=5432 dbname=... user=... sslmode=require" -v ON_ERROR_STOP=1 -f artifacts/migrations/needlebase-migrate.sql
# Run a bundle with the connection string from the environment (never on the command line in shared shells):
#   ./artifacts/migrations/efbundle --connection "$ConnectionStrings__DefaultConnection"
#
# Generating the script/bundle needs no database connection and no production secrets.
set -euo pipefail

cd "$(dirname "$0")/.."
mode="${1:-script}"
runtime="${2:-linux-x64}"
out="artifacts/migrations"

case "$mode" in
  script|bundle|list) ;;
  *) echo "Usage: $0 [script|bundle [runtime]|list]" >&2; exit 2 ;;
esac

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK not found on PATH (install the .NET 9 SDK)." >&2
  exit 127
fi

if [[ ! "$runtime" =~ ^(linux|win|osx)-(x64|arm64)$ ]]; then
  echo "Unknown runtime '$runtime' (use e.g. linux-x64 or win-x64)." >&2
  exit 2
fi

mkdir -p "$out"
dotnet tool restore >/dev/null

# The command must come before the options (dotnet ef <command> [options]).
ef() { dotnet tool run dotnet-ef "$@" --project src/NeedleBase.Infrastructure \
         --startup-project src/NeedleBase.Infrastructure --context ApplicationDbContext; }

case "$mode" in
  script)
    # Never leave an old or half-written script behind under the final name.
    rm -f "$out/needlebase-migrate.sql" "$out/needlebase-migrate.sql.tmp"
    ef migrations script --idempotent --output "$out/needlebase-migrate.sql.tmp"
    mv "$out/needlebase-migrate.sql.tmp" "$out/needlebase-migrate.sql"
    echo "Wrote $out/needlebase-migrate.sql (idempotent, one transaction; safe to re-run)."
    ;;
  bundle)
    ext=""
    if [[ $runtime == win-* ]]; then ext=".exe"; fi
    rm -f "$out/efbundle" "$out/efbundle.exe"
    ef migrations bundle --force --self-contained --target-runtime "$runtime" --output "$out/efbundle$ext"
    echo "Wrote the migration bundle to $out/ ($runtime)."
    ;;
  list)
    ef migrations list --no-connect
    ;;
  *)
    echo "Usage: $0 [script|bundle [runtime]|list]" >&2
    exit 2
    ;;
esac
