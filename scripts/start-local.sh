#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG_DIR="$ROOT_DIR/output/local-logs"
if ! mkdir -p "$LOG_DIR" 2>/dev/null || [ ! -w "$LOG_DIR" ]; then
  LOG_DIR="/tmp/hack13-local-logs"
  mkdir -p "$LOG_DIR"
fi

pids=()
STARTED_PG=0

have_cmd() {
  command -v "$1" >/dev/null 2>&1
}

log() {
  printf "[%s] %s\n" "$(date +%H:%M:%S)" "$*"
}

start_bg() {
  local name="$1"
  shift
  log "Starting $name..."
  ("$@") >"$LOG_DIR/${name}.log" 2>&1 &
  local pid=$!
  pids+=("$pid")
  log "$name started (pid $pid). Logs: $LOG_DIR/${name}.log"
}

start_postgres_if_available() {
  if ! have_cmd pg_ctl || ! have_cmd initdb; then
    log "Postgres tools not found (pg_ctl/initdb). Skipping local DB startup."
    return 0
  fi

  if have_cmd pg_isready && pg_isready -h localhost -p 5432 >/dev/null 2>&1; then
    log "Postgres already running on localhost:5432."
  else
    local pgdata="$ROOT_DIR/.pgdata"
    if [ ! -f "$pgdata/PG_VERSION" ]; then
      log "Initializing local Postgres data dir at $pgdata..."
      initdb -D "$pgdata" -U hack13 --auth=trust --encoding=UTF8 >/dev/null
    fi

    log "Starting local Postgres (port 5432)..."
    pg_ctl -D "$pgdata" -o "-p 5432" -w start >/dev/null
    STARTED_PG=1
  fi

  if ! have_cmd psql || ! have_cmd createdb; then
    log "psql/createdb not found; skipping schema/seed initialization."
    return 0
  fi

  if ! psql -h localhost -p 5432 -U hack13 -tAc "SELECT 1 FROM pg_database WHERE datname='hack13'" | grep -q 1; then
    log "Creating database hack13..."
    createdb -h localhost -p 5432 -U hack13 hack13
  fi

  log "Applying schema..."
  psql -h localhost -p 5432 -U hack13 -d hack13 -f "$ROOT_DIR/db/init/01_schema.sql" >/dev/null

  local table_exists
  table_exists=$(psql -h localhost -p 5432 -U hack13 -d hack13 -tAc "SELECT to_regclass('public.loans')")
  if [ "$table_exists" = "loans" ]; then
    local row_count
    row_count=$(psql -h localhost -p 5432 -U hack13 -d hack13 -tAc "SELECT COUNT(*) FROM loans")
    if [ "$row_count" = "0" ]; then
      log "Seeding data..."
      psql -h localhost -p 5432 -U hack13 -d hack13 -f "$ROOT_DIR/db/init/02_seed.sql" >/dev/null
    fi
  else
    log "Seeding data..."
    psql -h localhost -p 5432 -U hack13 -d hack13 -f "$ROOT_DIR/db/init/02_seed.sql" >/dev/null
  fi
}

cleanup() {
  log "Shutting down..."
  for pid in "${pids[@]:-}"; do
    if kill -0 "$pid" >/dev/null 2>&1; then
      kill "$pid" >/dev/null 2>&1 || true
    fi
  done

  if [ "$STARTED_PG" -eq 1 ] && have_cmd pg_ctl; then
    pg_ctl -D "$ROOT_DIR/.pgdata" -w stop >/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

log "Starting Hack13 (non-Docker) services from $ROOT_DIR"

start_postgres_if_available

if ! have_cmd dotnet; then
  log "dotnet not found. Install .NET 8 SDK to run the API and mock server."
  exit 1
fi

start_bg "mock-server" dotnet run --project "$ROOT_DIR/src/Hack13.TerminalServer"
start_bg "api" env ASPNETCORE_URLS="http://localhost:5000" ASPNETCORE_ENVIRONMENT="Development" dotnet run --project "$ROOT_DIR/src/Hack13.Api"

if ! have_cmd npm; then
  log "npm not found. Install Node.js 20+ to run the frontend."
  exit 1
fi

if [ ! -d "$ROOT_DIR/frontend/node_modules" ]; then
  log "Installing frontend dependencies..."
  (cd "$ROOT_DIR/frontend" && npm install) >/dev/null
fi

start_bg "frontend" env VITE_API_BASE_URL="http://localhost:5000" npm --prefix "$ROOT_DIR/frontend" run dev

log "All services started."
log "API: http://localhost:5000"
log "Frontend: http://localhost:5173"
log "Mock server: localhost:5250"
log "Press Ctrl+C to stop."

wait
