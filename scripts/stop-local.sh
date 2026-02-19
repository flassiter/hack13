#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

have_cmd() {
  command -v "$1" >/dev/null 2>&1
}

log() {
  printf "[%s] %s\n" "$(date +%H:%M:%S)" "$*"
}

kill_port() {
  local port="$1"
  if have_cmd lsof; then
    local pids
    pids=$(lsof -ti tcp:"$port" || true)
    if [ -n "$pids" ]; then
      log "Stopping process(es) on port $port: $pids"
      kill $pids >/dev/null 2>&1 || true
    fi
  elif have_cmd fuser; then
    log "Stopping process(es) on port $port (fuser)"
    fuser -k -n tcp "$port" >/dev/null 2>&1 || true
  else
    log "Neither lsof nor fuser available; cannot stop port $port cleanly."
  fi
}

log "Stopping Hack13 local services..."

kill_port 5173
kill_port 5000
kill_port 5250

if have_cmd pg_ctl && [ -f "$ROOT_DIR/.pgdata/PG_VERSION" ]; then
  log "Stopping local Postgres..."
  pg_ctl -D "$ROOT_DIR/.pgdata" -w stop >/dev/null || true
fi

log "Done."
