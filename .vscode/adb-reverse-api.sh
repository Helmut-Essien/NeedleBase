#!/usr/bin/env bash
# USB: map device localhost:5036 → host Api on :5036
set -euo pipefail

export PATH="${HOME}/Android/Sdk/platform-tools:${PATH}"

adb start-server >/dev/null
adb devices -l

if ! adb devices | awk 'NR>1 && $2=="device"{found=1} END{exit !found}'; then
  echo "ERROR: no authorized Android device. Unlock the phone and allow USB debugging." >&2
  exit 1
fi

adb reverse tcp:5036 tcp:5036
adb reverse --list
