#!/usr/bin/env bash
# Verifies the IDE launch.json contract for NeedleBase (Api + Mobile USB/Emulator):
# - USB + Emulator compounds exist with correct preLaunchTask / stopAll
# - Mobile preLaunchTask is exactly "maui: Build" (required by MAUI extension)
# - then runs the same preflight / build-api / Api DLL / Mobile Run / stop tasks
#   that the USB launch entries point at, against the connected phone.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
export PATH="${HOME}/Android/Sdk/platform-tools:${PATH}"

LOCAL_ENV="$ROOT/.vscode/needlebase.local.env"
if [ -f "$LOCAL_ENV" ]; then
  set -a
  # shellcheck disable=SC1090
  . "$LOCAL_ENV"
  set +a
fi

LAUNCH="$ROOT/.vscode/launch.json"
# USB launch pins no MAUI serial. After preflight, deploy uses .adb-device
# (the reverse target). NEEDLEBASE_DEVICE / local.env only influence preflight
# when exactly one phone is connected.
DEVICE=""
API_DLL="$ROOT/src/NeedleBase.Api/bin/Debug/net9.0/NeedleBase.Api.dll"
API_CWD="$ROOT/src/NeedleBase.Api"
PIDFILE="$ROOT/.vscode/api-dev.pid"
LOG="$ROOT/.vscode/api-dev.log"
DEVICE_FILE="$ROOT/.vscode/.adb-device"

python3 - <<'PY'
import json, sys
from pathlib import Path
launch = json.loads(Path(".vscode/launch.json").read_text())
configs = {c["name"]: c for c in launch["configurations"]}
compounds = {c["name"]: c for c in launch.get("compounds", [])}

compound = compounds.get("NeedleBase (Api + Mobile USB)")
assert compound, "missing compound NeedleBase (Api + Mobile USB)"
assert compound.get("configurations") == ["Api (Development)", "Mobile (Android USB)"], compound
assert compound.get("preLaunchTask") == "api-mobile-preflight", compound
assert compound.get("stopAll") is True

emu_compound = compounds.get("NeedleBase (Api + Mobile Emulator)")
assert emu_compound, "missing compound NeedleBase (Api + Mobile Emulator)"
assert emu_compound.get("configurations") == ["Api (Development)", "Mobile (Android Emulator)"], emu_compound
assert emu_compound.get("preLaunchTask") == "api-mobile-preflight-emulator", emu_compound
assert emu_compound.get("stopAll") is True

api = configs["Api (Development)"]
assert api["type"] == "coreclr"
assert api["preLaunchTask"] == "build-api"
assert api["postDebugTask"] == "api: stop"
assert api["program"].endswith("NeedleBase.Api.dll")
assert api["env"]["ASPNETCORE_URLS"] == "http://127.0.0.1:5036"

mobile = configs["Mobile (Android USB)"]
assert mobile["type"] == "maui"
assert mobile["preLaunchTask"] == "maui: Build", (
    "MAUI extension requires preLaunchTask exactly 'maui: Build'; got %r" % mobile.get("preLaunchTask")
)
assert mobile["postDebugTask"] == "mobile: stop"
assert mobile.get("targetFramework") == "net9.0-android"
assert mobile.get("project", "").endswith("src/NeedleBase.Mobile/NeedleBase.Mobile.csproj"), mobile.get("project")
assert "device" not in mobile, "USB launch must not pin a serial; it uses the single connected phone"
assert mobile.get("connectionType") == "usb"

emu = configs["Mobile (Android Emulator)"]
assert emu["type"] == "maui"
assert emu.get("device") == "emulator-5554"
assert emu.get("targetFramework") == "net9.0-android"
assert emu.get("project", "").endswith("src/NeedleBase.Mobile/NeedleBase.Mobile.csproj"), emu.get("project")
assert emu["preLaunchTask"] == "maui: Build", (
    "MAUI extension requires preLaunchTask exactly 'maui: Build'; got %r" % emu.get("preLaunchTask")
)
assert emu["postDebugTask"] == "mobile: stop"
assert emu.get("connectionType") == "usb"
print("launch.json contract OK")
PY

echo "===== compound preLaunch: api-mobile-preflight ====="
bash .vscode/api-mobile-preflight.sh usb

echo "===== device (must match preflight reverse target) ====="
adb devices -l
if [ -f "$DEVICE_FILE" ]; then
  DEVICE="$(tr -d '[:space:]' <"$DEVICE_FILE" || true)"
fi
if [ -z "$DEVICE" ]; then
  DEVICE="$(adb devices | awk 'NR>1 && $2=="device" && $1 !~ /^emulator-/ {print $1}')"
  [ "$(printf '%s' "$DEVICE" | grep -c .)" -eq 1 ] || { echo "Expected exactly one ready USB phone and no emulator (disconnect extras, then re-run preflight)"; exit 1; }
fi
echo "Using USB device: $DEVICE"
adb -s "$DEVICE" get-state | grep -qx device

echo "===== Api preLaunch: build-api ====="
dotnet build "$ROOT/src/NeedleBase.Api/NeedleBase.Api.csproj" --configuration Debug -v q
test -f "$API_DLL"

echo "===== Api debuggee (same DLL/cwd/env as launch.json) ====="
rm -f "$PIDFILE"
: >"$LOG"
(
  cd "$API_CWD"
  export ASPNETCORE_ENVIRONMENT=Development
  export ASPNETCORE_URLS=http://127.0.0.1:5036
  setsid dotnet exec "$API_DLL" >>"$LOG" 2>&1 < /dev/null &
  echo $! >"$PIDFILE"
)
ready=0
for _ in $(seq 1 90); do
  if curl -sf http://127.0.0.1:5036/health/live >/dev/null; then ready=1; break; fi
  sleep 0.5
done
[ "$ready" = 1 ] || { echo "Api failed to become healthy"; tail -80 "$LOG"; exit 1; }
curl -sf http://127.0.0.1:5036/health/ready >/dev/null
echo "Api healthy"

echo "===== Mobile deploy (net9.0-android Debug → $DEVICE) ====="
# Closest CLI equivalent to maui debugger deploy after 'maui: Build'
dotnet build "$ROOT/src/NeedleBase.Mobile/NeedleBase.Mobile.csproj" \
  -t:Run \
  -f net9.0-android \
  -p:Configuration=Debug \
  -p:AdbTarget="-s ${DEVICE}" \
  -p:AndroidSdkDirectory="${HOME}/Android/Sdk" \
  -p:JavaSdkDirectory=/usr/lib/jvm/java-17-openjdk-amd64 \
  -v m | tee /tmp/nb-ide-mobile-run.log | tail -40

sleep 4
APP_PID="$(adb -s "$DEVICE" shell pidof -s com.helmutcode.needlebase 2>/dev/null | tr -d '\r' || true)"
echo "app_pid=${APP_PID:-NONE}"
[ -n "${APP_PID:-}" ] || { echo "Mobile app did not start on device"; exit 1; }

echo "===== stay-up 15s (must not die after build) ====="
for t in 5 10 15; do
  sleep 5
  live=$(curl -sf http://127.0.0.1:5036/health/live >/dev/null && echo ok || echo DOWN)
  app="$(adb -s "$DEVICE" shell pidof -s com.helmutcode.needlebase 2>/dev/null | tr -d '\r' || true)"
  echo "t=${t}s api=$live app=${app:-NONE}"
  [ "$live" = ok ] && [ -n "${app:-}" ] || { echo "STAY_UP_FAIL"; exit 1; }
done

echo "===== Stop Debugging postDebugTasks (api: stop + mobile: stop) ====="
bash .vscode/stop-listener.sh 5036
bash .vscode/mobile-stop.sh
# also clear our pidfile launcher if still around
if [ -f "$PIDFILE" ]; then
  pid="$(cat "$PIDFILE" || true)"
  kill -TERM -"$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true
  rm -f "$PIDFILE"
fi
bash .vscode/stop-listener.sh 5036 || true
sleep 1

live=$(curl -sf http://127.0.0.1:5036/health/live >/dev/null && echo up || echo down)
app="$(adb -s "$DEVICE" shell pidof -s com.helmutcode.needlebase 2>/dev/null | tr -d '\r' || true)"
port=$(ss -H -ltn 'sport = :5036' 2>/dev/null | grep -c . || true)
echo "after_stop api=$live app=${app:-NONE} port=$port"
[ "$live" = down ] && [ -z "${app:-}" ] && [ "${port:-0}" = 0 ] || { echo STOP_FAIL; exit 1; }

echo "IDE_F5_VERIFY_OK"
