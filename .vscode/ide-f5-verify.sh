#!/usr/bin/env bash
# Verifies the IDE launch.json contract for NeedleBase (Api + Mobile):
# - compound exists with Api + Mobile
# - Mobile preLaunchTask is exactly "maui: Build" (required by MAUI extension)
# - then runs the same preflight / build-api / Api DLL / Mobile Run / stop tasks
#   that those launch entries point at, against the connected phone.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
export PATH="${HOME}/Android/Sdk/platform-tools:${PATH}"

LAUNCH="$ROOT/.vscode/launch.json"
DEVICE="${NEEDLEBASE_DEVICE:-R3CX80J9TFR}"
API_DLL="$ROOT/src/NeedleBase.Api/bin/Debug/net9.0/NeedleBase.Api.dll"
API_CWD="$ROOT/src/NeedleBase.Api"
PIDFILE="$ROOT/.vscode/api-dev.pid"
LOG="$ROOT/.vscode/api-dev.log"

python3 - <<'PY'
import json, sys
from pathlib import Path
launch = json.loads(Path(".vscode/launch.json").read_text())
configs = {c["name"]: c for c in launch["configurations"]}
compounds = {c["name"]: c for c in launch.get("compounds", [])}

compound = compounds.get("NeedleBase (Api + Mobile)")
assert compound, "missing compound NeedleBase (Api + Mobile)"
assert compound.get("configurations") == ["Api (Development)", "Mobile (Android)"], compound
assert compound.get("preLaunchTask") == "api-mobile-preflight", compound
assert compound.get("stopAll") is True

api = configs["Api (Development)"]
assert api["type"] == "coreclr"
assert api["preLaunchTask"] == "build-api"
assert api["postDebugTask"] == "api: stop"
assert api["program"].endswith("NeedleBase.Api.dll")
assert api["env"]["ASPNETCORE_URLS"] == "http://127.0.0.1:5036"

mobile = configs["Mobile (Android)"]
assert mobile["type"] == "maui"
assert mobile["preLaunchTask"] == "maui: Build", (
    "MAUI extension requires preLaunchTask exactly 'maui: Build'; got %r" % mobile.get("preLaunchTask")
)
assert mobile["postDebugTask"] == "mobile: stop"
assert mobile.get("targetFramework") == "net9.0-android"
assert mobile.get("device") == "R3CX80J9TFR"
assert mobile.get("connectionType") == "usb"
print("launch.json contract OK")
PY

echo "===== device ====="
adb start-server >/dev/null
adb devices -l
adb -s "$DEVICE" get-state | grep -qx device

echo "===== compound preLaunch: api-mobile-preflight ====="
docker compose up -d
bash .vscode/adb-reverse-api.sh
bash .vscode/stop-listener.sh 5036

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
