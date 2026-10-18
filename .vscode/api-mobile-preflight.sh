#!/usr/bin/env bash
# NeedleBase compound preflight (Api + Mobile).
# Usage: api-mobile-preflight.sh [usb|emulator]
# Logs every stage so a failed F5 explains itself in the task terminal
# and in .vscode/api-mobile.log. USB is the default path.
set -uo pipefail

MODE="${1:-usb}"
case "$MODE" in
  usb|emulator) ;;
  *)
    echo "[NeedleBase $(date '+%H:%M:%S')] ERROR: invalid mode '$MODE' (expected usb|emulator)." >&2
    exit 1
    ;;
esac

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
LOG_FILE="$ROOT/.vscode/api-mobile.log"
LOCAL_ENV="$ROOT/.vscode/needlebase.local.env"
if [ -f "$LOCAL_ENV" ]; then
  set -a
  # shellcheck disable=SC1090
  . "$LOCAL_ENV"
  set +a
fi
export PATH="${HOME}/Android/Sdk/platform-tools:${PATH}"
EXPECTED_EMU="${NEEDLEBASE_EMULATOR:-emulator-5554}"
AVD_NAME="${NEEDLEBASE_AVD:-Pixel_7_API35}"

log() {
  local line="[NeedleBase $(date '+%H:%M:%S')] $*"
  echo "$line"
  echo "[$(date '+%Y-%m-%d %H:%M:%S')] [preflight:$MODE] $*" >>"$LOG_FILE" 2>/dev/null || true
}

fail() {
  log "ERROR: $*"
  log "Preflight FAILED (mode=$MODE). Fix the problem above, then run the launch again. Full log: $LOG_FILE"
  exit 1
}

# Prints adb state for the launch-pinned emulator serial, or empty if absent.
pinned_emulator_state() {
  adb devices 2>/dev/null | awk -v pin="$EXPECTED_EMU" 'NR>1 && $1==pin {print $2; exit}'
}

# Space-separated other emulator serials that are already 'device' (not the pin).
other_ready_emulators() {
  adb devices 2>/dev/null | awk -v pin="$EXPECTED_EMU" 'NR>1 && $1 ~ /^emulator-/ && $1!=pin && $2=="device" {print $1}'
}

# Space-separated other emulator serials (any state, not the pin).
other_emulators() {
  adb devices 2>/dev/null | awk -v pin="$EXPECTED_EMU" 'NR>1 && $1 ~ /^emulator-/ && $1!=pin {print $1}'
}

log "=== Preflight started (mode=$MODE) ==="
log "Stage 1/3: starting Postgres (docker compose up -d)..."
COMPOSE_OUT="$(mktemp)"
if docker compose up -d >"$COMPOSE_OUT" 2>&1; then
  cat "$COMPOSE_OUT" >>"$LOG_FILE" 2>/dev/null || true
  log "Postgres: OK (docker compose is up)."
else
  cat "$COMPOSE_OUT" >>"$LOG_FILE" 2>/dev/null || true
  log "docker compose output (last lines):"
  tail -n 20 "$COMPOSE_OUT" 2>/dev/null | while IFS= read -r line; do log "  $line"; done
  rm -f "$COMPOSE_OUT"
  fail "Postgres failed to start. Is Docker running? Try 'docker compose up -d' in $ROOT."
fi
rm -f "$COMPOSE_OUT"

log "Stage 2/3: finding Android device (${MODE}) and setting up adb reverse..."
if [ "$MODE" = "emulator" ]; then
  # Longer wait for cold emulator boots unless the caller already set a value.
  export NEEDLEBASE_ADB_WAIT_SECONDS="${NEEDLEBASE_ADB_WAIT_SECONDS:-180}"

  PIN_STATE="$(pinned_emulator_state)"
  OTHERS="$(other_emulators | tr '\n' ' ' | sed 's/[[:space:]]*$//')"
  OTHERS_READY="$(other_ready_emulators | tr '\n' ' ' | sed 's/[[:space:]]*$//')"

  if [ -n "$OTHERS_READY" ] && [ "$PIN_STATE" != "device" ]; then
    fail "Ready emulator(s) connected ($OTHERS_READY) but launch pin $EXPECTED_EMU is '${PIN_STATE:-missing}'. Stop the extras ('adb -s <serial> emu kill') or update launch.json device + NEEDLEBASE_EMULATOR together."
  fi
  if [ -n "$OTHERS" ] && [ -z "$PIN_STATE" ]; then
    fail "Other emulator(s) connected ($OTHERS) but launch pin $EXPECTED_EMU is missing. Stop them ('adb -s <serial> emu kill') so $EXPECTED_EMU can start, or update launch.json device + NEEDLEBASE_EMULATOR together."
  fi

  case "$PIN_STATE" in
    device)
      log "Pinned emulator $EXPECTED_EMU is already 'device' - will not start another instance."
      ;;
    offline)
      log "Pinned emulator $EXPECTED_EMU is offline (likely still booting) - will not start another instance."
      ;;
    "")
      EMU="$HOME/Android/Sdk/emulator/emulator"
      if [ ! -x "$EMU" ]; then
        fail "Emulator binary not found at $EMU. Install Android Emulator via SDK Manager, or use 'NeedleBase (Api + Mobile USB)'."
      fi
      if ! "$EMU" -list-avds 2>/dev/null | grep -qx "$AVD_NAME"; then
        log "Available AVDs:"
        "$EMU" -list-avds 2>/dev/null | while IFS= read -r line; do log "  $line"; done
        fail "AVD '$AVD_NAME' not found. Create it in Android Studio Device Manager, or set NEEDLEBASE_AVD to an existing AVD name."
      fi
      log "Pinned emulator $EXPECTED_EMU not listed - starting $AVD_NAME in the background (first boot can take several minutes)..."
      nohup "$EMU" -avd "$AVD_NAME" -no-snapshot-save >>"$LOG_FILE" 2>&1 &
      EMU_PID=$!
      sleep 2
      if ! kill -0 "$EMU_PID" 2>/dev/null; then
        log "Emulator process exited immediately. Last log lines:"
        tail -n 30 "$LOG_FILE" 2>/dev/null | while IFS= read -r line; do log "  $line"; done
        fail "Failed to start AVD $AVD_NAME (process died). See $LOG_FILE."
      fi
      log "Emulator process started (pid $EMU_PID). Waiting for $EXPECTED_EMU to become 'device'..."
      ;;
    *)
      fail "Pinned emulator $EXPECTED_EMU is in unexpected adb state '$PIN_STATE'. Fix USB/emulator debugging for that serial, or kill it and re-run."
      ;;
  esac
fi

if bash .vscode/adb-reverse-api.sh "$MODE"; then
  log "Device + adb reverse: OK."
else
  if [ "$MODE" = "emulator" ]; then
    fail "Emulator / adb reverse failed (expected $EXPECTED_EMU). See the guidance above — wait for boot, stop conflicting emulators, check $LOG_FILE, or set NEEDLEBASE_ADB_WAIT_SECONDS in .vscode/needlebase.local.env."
  else
    fail "USB / adb reverse failed. See the guidance above — common causes: no phone, multiple phones, or an emulator still running while using the USB launch."
  fi
fi

log "Stage 3/3: clearing any old Api still listening on port 5036..."
bash .vscode/stop-listener.sh 5036 || log "Nothing was listening on 5036 (that's fine)."
log "=== Preflight OK - VS Code will now build and start the Api, then deploy Mobile. ==="
