#!/usr/bin/env bash
# Manual / alternate stop helper for Api + Mobile (compounds use stopAll → api: stop + mobile: stop).
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
DEVICE_FILE="$ROOT/.vscode/.adb-device"
DEVICE=""
if [ -f "$DEVICE_FILE" ]; then
  DEVICE="$(tr -d '[:space:]' <"$DEVICE_FILE" || true)"
fi
if [ -n "$DEVICE" ]; then
  if adb -s "$DEVICE" get-state 2>/dev/null | grep -qx device; then
    adb -s "$DEVICE" shell am force-stop "$APP_ID" 2>/dev/null || true
    adb -s "$DEVICE" reverse --remove tcp:5036 2>/dev/null || true
    echo "Mobile app force-stopped on $DEVICE; adb reverse removed."
  else
    echo "WARNING: saved serial $DEVICE is not ready — skipped force-stop (will not target another device). App/reverse may still be active on that device if it reconnects." >&2
  fi
elif adb devices | awk 'NR>1 && $2=="device"{found=1} END{exit !found}'; then
  adb shell am force-stop "$APP_ID" 2>/dev/null || true
  adb reverse --remove tcp:5036 2>/dev/null || true
  echo "Mobile app force-stopped; adb reverse removed."
else
  echo "No adb device — skipped app force-stop."
fi
rm -f "$DEVICE_FILE"

if ss -H -ltn "sport = :5036" 2>/dev/null | grep -q .; then
  echo "WARNING: something still listening on :5036" >&2
  ss -H -ltnp "sport = :5036" >&2 || true
  exit 1
fi

echo "NeedleBase Api + Mobile stopped."
