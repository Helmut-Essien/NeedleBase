#!/usr/bin/env bash
# NeedleBase: set up `adb reverse tcp:5036 tcp:5036` for the Api.
#
# Usage:
#   adb-reverse-api.sh [usb|emulator|any]
#
# Modes:
#   usb      (default) - physical USB device only. Requires exactly one
#            ready phone and no ready emulator (MAUI USB launch has no
#            pinned device). Optional NEEDLEBASE_DEVICE must match that
#            single phone if set. Waits up to NEEDLEBASE_ADB_WAIT_SECONDS
#            (default 60).
#   emulator - Android emulator only. Prefers NEEDLEBASE_EMULATOR
#            (default emulator-5554). Default wait is 180s unless overridden.
#   any      - USB preferred serial first, then preferred emulator, then any.
set -uo pipefail

MODE="${1:-usb}"
case "$MODE" in
  usb|emulator|any) ;;
  *)
    echo "[NeedleBase $(date '+%H:%M:%S')] ERROR: invalid mode '$MODE' (expected usb|emulator|any)." >&2
    exit 1
    ;;
esac

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
LOG_FILE="$ROOT/.vscode/api-mobile.log"
DEVICE_FILE="$ROOT/.vscode/.adb-device"
LOCAL_ENV="$ROOT/.vscode/needlebase.local.env"

# Optional local overrides for F5 tasks (VS Code does not inherit integrated-terminal exports).
if [ -f "$LOCAL_ENV" ]; then
  set -a
  # shellcheck disable=SC1090
  . "$LOCAL_ENV"
  set +a
fi

# Emulator first-boot often exceeds 60s; keep USB waits snappy unless overridden.
if [ -n "${NEEDLEBASE_ADB_WAIT_SECONDS+x}" ]; then
  WAIT_SECONDS="$NEEDLEBASE_ADB_WAIT_SECONDS"
elif [ "$MODE" = "emulator" ]; then
  WAIT_SECONDS=180
else
  WAIT_SECONDS=60
fi

EXPECTED_USB="${NEEDLEBASE_DEVICE:-}"  # empty = accept any single USB phone
EXPECTED_EMU="${NEEDLEBASE_EMULATOR:-emulator-5554}"
AVD_NAME="${NEEDLEBASE_AVD:-Pixel_7_API35}"

export PATH="${HOME}/Android/Sdk/platform-tools:${PATH}"

log() {
  local line="[NeedleBase $(date '+%H:%M:%S')] $*"
  echo "$line"
  echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" >>"$LOG_FILE" 2>/dev/null || true
}

# Prints "serial<TAB>state" lines for connected adb targets.
list_devices() {
  adb devices 2>/dev/null | awk 'NR>1 && NF>=2 {print $1"\t"$2}'
}

# Collect ready USB / emulator serials into globals USB_SERIALS / EMU_SERIALS
# (space-separated). Prefers expected serials when present.
collect_ready() {
  USB_SERIALS=""
  EMU_SERIALS=""
  local serial state
  while IFS=$'\t' read -r serial state; do
    [ "$state" = "device" ] || continue
    case "$serial" in
      emulator-*) EMU_SERIALS="${EMU_SERIALS:+$EMU_SERIALS }$serial" ;;
      *)          USB_SERIALS="${USB_SERIALS:+$USB_SERIALS }$serial" ;;
    esac
  done < <(list_devices)
}

has_serial() {
  local needle="$1" haystack="$2" s
  for s in $haystack; do
    [ "$s" = "$needle" ] && return 0
  done
  return 1
}

first_serial() {
  # Prints first token of $1, or empty.
  local list="$1"
  # shellcheck disable=SC2086
  set -- $list
  echo "${1:-}"
}

count_serials() {
  # Prints the number of space-separated serials in $1.
  local list="$1" n=0 x
  for x in $list; do n=$((n + 1)); done
  echo "$n"
}

pick_device() {
  # $1 = usb|emulator|any ; prints the chosen serial, or nothing.
  # usb: exactly one ready phone (optional NEEDLEBASE_DEVICE must equal it);
  # emulator: only the launch.json pin ($EXPECTED_EMU, default emulator-5554).
  local mode="$1"
  collect_ready

  case "$mode" in
    usb)
      if [ "$(count_serials "$USB_SERIALS")" -ne 1 ]; then
        :
      elif [ -n "$EXPECTED_USB" ]; then
        if has_serial "$EXPECTED_USB" "$USB_SERIALS"; then
          echo "$EXPECTED_USB"
        fi
      else
        echo "$(first_serial "$USB_SERIALS")"
      fi
      ;;
    emulator)
      if has_serial "$EXPECTED_EMU" "$EMU_SERIALS"; then
        echo "$EXPECTED_EMU"
      fi
      ;;
    any)
      if [ -n "$EXPECTED_USB" ] && has_serial "$EXPECTED_USB" "$USB_SERIALS"; then
        echo "$EXPECTED_USB"
      elif [ -n "$USB_SERIALS" ]; then
        echo "$(first_serial "$USB_SERIALS")"
      elif has_serial "$EXPECTED_EMU" "$EMU_SERIALS"; then
        echo "$EXPECTED_EMU"
      else
        echo "$(first_serial "$EMU_SERIALS")"
      fi
      ;;
  esac
}

device_state_hint() {
  # Explains unauthorized/offline targets so the user knows what to fix.
  local serial state
  while IFS=$'\t' read -r serial state; do
    case "$state" in
      unauthorized)
        log "Device $serial is UNAUTHORIZED - unlock the phone and tap 'Allow' on the 'Allow USB debugging?' prompt, then re-run." ;;
      offline)
        case "$serial" in
          emulator-*)
            log "Emulator $serial is OFFLINE (still booting or stalled). Wait for the emulator window, or check $LOG_FILE for emulator output." ;;
          *)
            log "Device $serial is OFFLINE - unplug/replug the USB cable, or toggle USB debugging off/on in Developer options." ;;
        esac
        ;;
    esac
  done < <(list_devices)
}

log_mode_wait_hint() {
  local elapsed="$1"
  case "$MODE" in
    emulator)
      log "No emulator ready yet (${elapsed}s/${WAIT_SECONDS}s). Waiting for launch pin $EXPECTED_EMU only. Checking again..."
      ;;
    usb)
      if [ -n "$EXPECTED_USB" ]; then
        log "No USB device ready yet (${elapsed}s/${WAIT_SECONDS}s). Plug in your phone with a data USB cable, unlock it, and allow USB debugging (pinned serial: $EXPECTED_USB). Checking again..."
      else
        log "No USB device recognised yet (${elapsed}s/${WAIT_SECONDS}s). Plug in any phone with a data USB cable, unlock it, and allow USB debugging. Checking again..."
      fi
      ;;
    any)
      log "No Android device ready yet (${elapsed}s/${WAIT_SECONDS}s). Prefer any USB phone, else emulator ($EXPECTED_EMU). Checking again..."
      ;;
  esac
}

log_timeout_recovery() {
  log "ERROR: no usable Android device found after ${WAIT_SECONDS}s (mode=$MODE)."
  log "What VS Code sees right now:"
  adb devices -l 2>&1 | while IFS= read -r line; do log "  adb: $line"; done
  device_state_hint
  case "$MODE" in
    usb)
      if [ -n "$EXPECTED_USB" ]; then
        log "Fix: 1) connect phone $EXPECTED_USB by USB (charge-only cables will not work), 2) unlock it, 3) tap Allow on the USB-debugging prompt, 4) confirm 'adb devices' shows that serial alone as 'device' (no second phone, no emulator), 5) run 'NeedleBase (Api + Mobile USB)' again."
      else
        log "Fix: 1) connect exactly one phone by USB (charge-only cables will not work), 2) unlock it, 3) tap Allow on the USB-debugging prompt, 4) confirm 'adb devices' shows exactly one phone as 'device' and no emulator, 5) run 'NeedleBase (Api + Mobile USB)' again."
      fi
      log "Tip: no phone handy? Use the first-class 'NeedleBase (Api + Mobile Emulator)' launch instead."
      ;;
    emulator)
      log "Fix: 1) confirm AVD $AVD_NAME exists ('emulator -list-avds'), 2) stop other emulators if they stole the port, 3) wait for the emulator UI to finish booting, 4) confirm 'adb devices' shows $EXPECTED_EMU as 'device', 5) re-run 'NeedleBase (Api + Mobile Emulator)'."
      log "Tip: set NEEDLEBASE_ADB_WAIT_SECONDS=300 in .vscode/needlebase.local.env (terminal export does not reach VS Code tasks), or check $LOG_FILE for emulator startup errors."
      ;;
    any)
      log "Fix: connect a USB phone or start an emulator, then re-run the launch."
      ;;
  esac
}

log_wrong_device_hint() {
  # Once per wait loop: explain why present devices were not picked.
  collect_ready
  case "$MODE" in
    usb)
      if [ -n "$EXPECTED_USB" ] && [ -n "$USB_SERIALS" ] && ! has_serial "$EXPECTED_USB" "$USB_SERIALS"; then
        log "Connected USB device(s): $USB_SERIALS — waiting for pinned serial $EXPECTED_USB (unset NEEDLEBASE_DEVICE to accept any single phone)."
      elif [ "$(count_serials "$USB_SERIALS")" -gt 1 ]; then
        log "Connected USB devices: $USB_SERIALS — more than one phone is connected. Disconnect all but one so status-bar deploy can match adb reverse."
      fi
      ;;
    emulator)
      if [ -n "$EMU_SERIALS" ] && ! has_serial "$EXPECTED_EMU" "$EMU_SERIALS"; then
        log "Connected emulator(s): $EMU_SERIALS — waiting for launch pin $EXPECTED_EMU (stop extras or update launch.json + NEEDLEBASE_EMULATOR)."
      fi
      ;;
  esac
}

log "adb-reverse-api: mode=$MODE, waiting up to ${WAIT_SECONDS}s."
log "Full log: $LOG_FILE"

if ! command -v adb >/dev/null 2>&1; then
  log "ERROR: adb not found. Expected it under ~/Android/Sdk/platform-tools - is the Android SDK installed?"
  exit 1
fi

adb start-server >/dev/null 2>&1 || log "WARNING: 'adb start-server' failed; continuing anyway."

usb_fail_emulator_ready() {
  log "ERROR: emulator(s) are ready ($EMU_SERIALS) while using USB mode."
  log "Mobile (Android USB) has no pinned device — a ready emulator can steal status-bar deploy."
  local s
  for s in $EMU_SERIALS; do
    log "Fix: stop it with: adb -s $s emu kill"
  done
  log "Or use 'NeedleBase (Api + Mobile Emulator)' instead."
  exit 1
}

# USB F5 needs exactly one phone and no emulator: MAUI status bar is not pinned.
if [ "$MODE" = "usb" ]; then
  collect_ready
  if [ -n "$EMU_SERIALS" ]; then
    usb_fail_emulator_ready
  fi
  if [ "$(count_serials "$USB_SERIALS")" -gt 1 ]; then
    log "ERROR: more than one USB phone is connected ($USB_SERIALS)."
    log "Disconnect all but one phone, then run 'NeedleBase (Api + Mobile USB)' again (NEEDLEBASE_DEVICE cannot pin MAUI deploy)."
    exit 1
  fi
fi

SELECTED=""
elapsed=0
WRONG_HINT_SHOWN=0
STATE_HINT_SHOWN=0
while :; do
  # Re-check multi-phone / ready-emulator during wait (targets may appear mid-poll).
  if [ "$MODE" = "usb" ]; then
    collect_ready
    if [ -n "$EMU_SERIALS" ]; then
      usb_fail_emulator_ready
    fi
    if [ "$(count_serials "$USB_SERIALS")" -gt 1 ]; then
      log "ERROR: more than one USB phone is connected ($USB_SERIALS)."
      log "Disconnect all but one phone, then run 'NeedleBase (Api + Mobile USB)' again (NEEDLEBASE_DEVICE cannot pin MAUI deploy)."
      exit 1
    fi
  fi

  SELECTED="$(pick_device "$MODE")"
  [ -n "$SELECTED" ] && break

  if [ "$elapsed" -ge "$WAIT_SECONDS" ]; then
    break
  fi

  log_mode_wait_hint "$elapsed"
  log_wrong_device_hint
  if [ "$STATE_HINT_SHOWN" -eq 0 ]; then
    device_state_hint
    STATE_HINT_SHOWN=1
  fi
  sleep 5
  elapsed=$((elapsed + 5))
done

if [ -z "$SELECTED" ]; then
  log_timeout_recovery
  exit 1
fi

case "$MODE" in
  usb)
    if [ -n "$EXPECTED_USB" ]; then
      log "Selected USB device $SELECTED (pinned via NEEDLEBASE_DEVICE)."
    else
      log "Selected USB device $SELECTED (the single connected phone)."
    fi
    # USB launch.json pins no MAUI serial (status bar chooses deploy target).
    # Extra ready phones/emulators can steal deploy while reverse stays on $SELECTED.
    collect_ready
    if [ "$(count_serials "$USB_SERIALS")" -gt 1 ]; then
      log "ERROR: USB reverse will target $SELECTED, but multiple phones are ready: $USB_SERIALS."
      log "Mobile (Android USB) has no pinned device — the status bar may deploy to a different phone, so Api calls fail."
      log "Fix: disconnect every phone except $SELECTED (NEEDLEBASE_DEVICE alone cannot pin MAUI deploy)."
      exit 1
    fi
    if [ -n "$EMU_SERIALS" ]; then
      usb_fail_emulator_ready
    fi
    log "Status-bar check: set NeedleBase.Mobile target to phone $SELECTED so MAUI deploy matches this reverse."
    ;;
  emulator)
    log "Selected emulator $SELECTED (matches launch pin $EXPECTED_EMU)."
    ;;
  any)
    case "$SELECTED" in
      emulator-*) log "Selected emulator $SELECTED (mode=any; preferred pin was $EXPECTED_EMU)." ;;
      *)          log "Selected USB device $SELECTED (mode=any)." ;;
    esac
    ;;
esac

MODEL="$(adb -s "$SELECTED" shell getprop ro.product.model 2>/dev/null | tr -d '\r' || true)"
[ -n "$MODEL" ] && log "Device model: $MODEL"

if adb -s "$SELECTED" reverse tcp:5036 tcp:5036; then
  log "adb reverse OK: device localhost:5036 -> host 127.0.0.1:5036 for $SELECTED"
else
  log "ERROR: 'adb reverse tcp:5036 tcp:5036' failed for $SELECTED. The mobile app will not reach the Api at http://localhost:5036/."
  exit 1
fi

adb -s "$SELECTED" reverse --list 2>/dev/null | while IFS= read -r line; do
  [ -n "$line" ] && log "reverse --list: $line"
done

# Persist serial for stop scripts in later tasks / shells.
echo "$SELECTED" >"$DEVICE_FILE"
log "adb-reverse-api: done. Device serial saved to .vscode/.adb-device"
