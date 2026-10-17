#!/usr/bin/env bash
# Compound preLaunch for NeedleBase (Api + Mobile): start Postgres + Api detached, then exit
# so Mobile's direct preLaunchTask (maui: Build) can run next.
# Api survives the task terminal closing via setsid; postDebugTask "api-mobile: stop" kills it.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

export PATH="${HOME}/Android/Sdk/platform-tools:${PATH}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5036}"

LOG="$ROOT/.vscode/api-dev.log"
PIDFILE="$ROOT/.vscode/api-dev.pid"
API_DLL="$ROOT/src/NeedleBase.Api/bin/Debug/net9.0/NeedleBase.Api.dll"
API_CWD="$ROOT/src/NeedleBase.Api"

echo "Starting Postgres (docker compose)..."
docker compose up -d

adb start-server >/dev/null 2>&1 || true
if ! adb devices | awk 'NR>1 && $2=="device"{found=1} END{exit !found}'; then
  echo "ERROR: no authorized Android device (adb devices). Unlock phone and allow USB debugging." >&2
  adb devices -l >&2 || true
  exit 1
fi

adb reverse tcp:5036 tcp:5036
echo "adb reverse tcp:5036 tcp:5036 OK"
adb reverse --list

bash .vscode/stop-listener.sh 5036
rm -f "$PIDFILE"

echo "Building Api..."
dotnet build "$ROOT/src/NeedleBase.Api/NeedleBase.Api.csproj" --configuration Debug -v q

if [ ! -f "$API_DLL" ]; then
  echo "ERROR: missing $API_DLL" >&2
  exit 1
fi

echo "Starting Api on ${ASPNETCORE_URLS} (detached)..."
: >"$LOG"
# New session so closing the task terminal does not SIGHUP the Api.
# Use the built DLL (same as coreclr launch) — avoids nested "dotnet run" process quirks.
(
  cd "$API_CWD"
  setsid dotnet exec "$API_DLL" >>"$LOG" 2>&1 < /dev/null &
  echo $! >"$PIDFILE"
)

ready=0
for _ in $(seq 1 90); do
  if ! kill -0 "$(cat "$PIDFILE")" 2>/dev/null; then
    echo "Api process exited before becoming ready. Log:" >&2
    tail -80 "$LOG" >&2 || true
    exit 1
  fi
  if curl -sf "http://127.0.0.1:5036/health/live" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 0.5
done

if [ "$ready" -ne 1 ]; then
  echo "Timed out waiting for Api /health/live on :5036. Log:" >&2
  tail -80 "$LOG" >&2 || true
  exit 1
fi

echo "NeedleBase Api ready — leave this terminal open; Stop debugging runs api-mobile: stop"
exit 0
