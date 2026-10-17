#!/usr/bin/env bash
# Starts Postgres + Api for manual / alternate launch paths and stays alive until Api exits.
# Prints "NeedleBase Api ready" when /health/live succeeds.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

export PATH="${HOME}/Android/Sdk/platform-tools:${PATH}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5036}"

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

echo "Building Api..."
dotnet build "$ROOT/src/NeedleBase.Api/NeedleBase.Api.csproj" --configuration Debug -v q

echo "Starting Api on ${ASPNETCORE_URLS}..."
dotnet run --no-build \
  --project "$ROOT/src/NeedleBase.Api/NeedleBase.Api.csproj" \
  --launch-profile http &
API_PID=$!

cleanup() {
  if kill -0 "$API_PID" 2>/dev/null; then
    kill -TERM "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
  fi
  bash .vscode/stop-listener.sh 5036 || true
}
trap cleanup EXIT INT TERM

ready=0
for _ in $(seq 1 90); do
  if ! kill -0 "$API_PID" 2>/dev/null; then
    echo "Api process exited before becoming ready" >&2
    wait "$API_PID" || true
    exit 1
  fi
  if curl -sf "http://127.0.0.1:5036/health/live" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 0.5
done

if [ "$ready" -ne 1 ]; then
  echo "Timed out waiting for Api /health/live on :5036" >&2
  exit 1
fi

echo "NeedleBase Api ready"
wait "$API_PID"
