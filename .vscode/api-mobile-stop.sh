#!/usr/bin/env bash
# postDebugTask for NeedleBase (Api + Mobile): stop Api + force-stop the phone app.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

export PATH="${HOME}/Android/Sdk/platform-tools:${PATH}"

PIDFILE="$ROOT/.vscode/api-dev.pid"
APP_ID="com.helmutcode.needlebase"

echo "Stopping NeedleBase Api on :5036..."
if [ -f "$PIDFILE" ]; then
  pid="$(cat "$PIDFILE" 2>/dev/null || true)"
  if [ -n "${pid:-}" ] && kill -0 "$pid" 2>/dev/null; then
    # Kill the setsid'd "dotnet run" process group if possible.
    kill -TERM -"$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true
    sleep 0.4
    kill -KILL -"$pid" 2>/dev/null || kill -KILL "$pid" 2>/dev/null || true
  fi
  rm -f "$PIDFILE"
fi

bash .vscode/stop-listener.sh 5036 || true

# Also sweep any leftover NeedleBase.Api / dotnet run for this project.
pkill -f "NeedleBase.Api/NeedleBase.Api.csproj" 2>/dev/null || true
pkill -f "bin/Debug/net9.0/NeedleBase.Api" 2>/dev/null || true
bash .vscode/stop-listener.sh 5036 || true

echo "Force-stopping Mobile app on device (if any)..."
if adb devices | awk 'NR>1 && $2=="device"{found=1} END{exit !found}'; then
  adb shell am force-stop "$APP_ID" 2>/dev/null || true
  adb reverse --remove tcp:5036 2>/dev/null || true
  echo "Mobile app force-stopped; adb reverse removed."
else
  echo "No adb device — skipped app force-stop."
fi

if ss -H -ltn "sport = :5036" 2>/dev/null | grep -q .; then
  echo "WARNING: something still listening on :5036" >&2
  ss -H -ltnp "sport = :5036" >&2 || true
  exit 1
fi

echo "NeedleBase Api + Mobile stopped."
