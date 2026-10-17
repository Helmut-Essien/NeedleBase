#!/bin/sh
# Cursor's C# debugger launches into the integrated terminal and leaves that process running after Stop.
# Close whatever is still bound to the port the debug session started.
port=$1
if [ -z "$port" ]; then
  echo "usage: stop-listener.sh <port>" >&2
  exit 1
fi

pids=$(ss -H -ltnp "sport = :$port" 2>/dev/null | grep -oE 'pid=[0-9]+' | cut -d= -f2 | sort -u)
if [ -z "$pids" ] && command -v fuser >/dev/null 2>&1; then
  # ss may omit PIDs in some environments; fuser is a reliable fallback.
  pids=$(fuser "${port}/tcp" 2>/dev/null | tr -s ' ' '\n' | grep -E '^[0-9]+$' | sort -u)
fi

if [ -z "$pids" ]; then
  exit 0
fi

# shellcheck disable=SC2086
kill -TERM $pids 2>/dev/null || true
sleep 0.4
pids=$(ss -H -ltnp "sport = :$port" 2>/dev/null | grep -oE 'pid=[0-9]+' | cut -d= -f2 | sort -u)
if [ -z "$pids" ] && command -v fuser >/dev/null 2>&1; then
  pids=$(fuser "${port}/tcp" 2>/dev/null | tr -s ' ' '\n' | grep -E '^[0-9]+$' | sort -u)
fi
if [ -n "$pids" ]; then
  # shellcheck disable=SC2086
  kill -KILL $pids 2>/dev/null || true
fi
