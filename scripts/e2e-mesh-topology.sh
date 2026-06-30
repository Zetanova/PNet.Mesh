#!/usr/bin/env bash
# Run the Docker Compose multi-node mesh topology e2e smoke test.
# opt-status: check-required (new compose e2e runner)
# opt-date: 2026-06-30
# forks: setup=1, compose=1 per operation, poll=1/log read

set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/e2e-mesh-topology.sh [options]

Builds and starts the six-node Docker Compose topology, waits until each test
node reports full peer pong coverage, then tears the topology down.

Options:
  --project-name NAME  Compose project name. Default: pnet-mesh-e2e
  --run-seconds N      Node runtime before it reports pong counts. Default: 30
  --timeout N          Overall startup/assertion timeout in seconds. Default: 120
  --no-build           Reuse the current image instead of building.
  --keep               Leave containers/networks behind for debugging.
  -h, --help           Show this help.
USAGE
}

die() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"
}

project_name="${PNET_MESH_E2E_PROJECT:-pnet-mesh-e2e}"
run_seconds="${PNET_MESH_E2E_RUN_SECONDS:-30}"
timeout_seconds="${PNET_MESH_E2E_TIMEOUT_SECONDS:-120}"
build=true
keep=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project-name)
      [[ $# -ge 2 ]] || die "--project-name requires a value"
      project_name="$2"
      shift 2
      ;;
    --run-seconds)
      [[ $# -ge 2 ]] || die "--run-seconds requires a value"
      run_seconds="$2"
      shift 2
      ;;
    --timeout)
      [[ $# -ge 2 ]] || die "--timeout requires a value"
      timeout_seconds="$2"
      shift 2
      ;;
    --no-build)
      build=false
      shift
      ;;
    --keep)
      keep=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      die "unknown argument: $1"
      ;;
  esac
done

[[ "$run_seconds" =~ ^[0-9]+$ ]] || die "--run-seconds must be a positive integer"
[[ "$timeout_seconds" =~ ^[0-9]+$ ]] || die "--timeout must be a positive integer"
(( run_seconds > 0 )) || die "--run-seconds must be greater than zero"
(( timeout_seconds > run_seconds )) || die "--timeout must be greater than --run-seconds"

require_command docker
require_command grep
require_command timeout

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

export PNET_MESH_E2E_RUN_SECONDS="$run_seconds"

compose=(docker compose -f docker-compose.yml -f docker-compose.e2e.yml -p "$project_name")
nodes=(node00 node01 node10 node11 node20 node21)
expected_routes=(
  'ping from node21 to node20'
  'pong from node20 to node21'
  'node21 got 1 pongs'
)

cleanup() {
  if [[ "$keep" == false ]]; then
    timeout 30s "${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

timeout 30s "${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true

up_args=(up -d)
if [[ "$build" == true ]]; then
  up_args+=(--build)
fi

timeout "${timeout_seconds}s" "${compose[@]}" "${up_args[@]}"

deadline=$((SECONDS + timeout_seconds))
while (( SECONDS < deadline )); do
  logs="$(timeout 30s "${compose[@]}" logs --no-color 2>&1 || true)"
  missing=()

  for node in "${nodes[@]}"; do
    if ! grep -q "Node\\[$node\\] started" <<<"$logs"; then
      missing+=("$node started")
    fi

    if ! grep -Eq "${node} got [0-9]+ pongs" <<<"$logs"; then
      missing+=("$node final pong count")
    fi
  done

  for route in "${expected_routes[@]}"; do
    if ! grep -q "$route" <<<"$logs"; then
      missing+=("$route")
    fi
  done

  if (( ${#missing[@]} == 0 )); then
    printf '%s\n' "$logs" |
      grep -E 'Node\[|got [0-9]+ pongs|ping from|pong from' |
      tail -n 120
    printf 'mesh topology e2e passed: all nodes started and node21 reached node20\n'
    exit 0
  fi

  sleep 2
done

printf 'error: timed out waiting for expected route pongs\n' >&2
if (( ${#missing[@]} == 0 )); then
  missing=("${nodes[@]}")
fi
printf 'missing nodes:' >&2
printf ' %s' "${missing[@]}" >&2
printf '\n\ncompose status:\n' >&2
timeout 30s "${compose[@]}" ps >&2 || true
printf '\nlast logs:\n' >&2
timeout 30s "${compose[@]}" logs --no-color --tail=200 >&2 || true
exit 1

# --- Testing ---
# Inputs: Docker Compose v2, Docker daemon access, .NET SDK/runtime images.
# Key functions: builds the test-node image, starts six compose services, asserts staged route pongs.
# Edge cases: bounded compose/log calls, cleanup on failure, --keep for post-failure inspection.
# Run: timeout 180s scripts/e2e-mesh-topology.sh
