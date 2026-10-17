#!/usr/bin/env bash
# postDebugTask for Mobile (Android): force-stop app + clear adb reverse.
set -euo pipefail

export PATH="${HOME}/Android/Sdk/platform-tools:${PATH}"
APP_ID="com.helmutcode.needlebase"

if adb devices | awk 'NR>1 && $2=="device"{found=1} END{exit !found}'; then
  adb shell am force-stop "$APP_ID" 2>/dev/null || true
  adb reverse --remove tcp:5036 2>/dev/null || true
  echo "Mobile app force-stopped; adb reverse removed."
else
  echo "No adb device — skipped app force-stop."
fi
