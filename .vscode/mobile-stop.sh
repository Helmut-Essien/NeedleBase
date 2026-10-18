#!/usr/bin/env bash
# postDebugTask for Mobile (Android USB / Emulator): force-stop app + clear adb reverse.
set -euo pipefail

export PATH="${HOME}/Android/Sdk/platform-tools:${PATH}"
APP_ID="com.helmutcode.needlebase"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
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

# Drop stale pin so a later USB/emulator session does not target the wrong serial.
rm -f "$DEVICE_FILE"
