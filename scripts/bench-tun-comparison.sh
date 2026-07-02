#!/usr/bin/env bash
# Run PNet.Mesh.Tun versus wireguard-go benchmark comparison artifacts.
# opt-status: optimized
# opt-date: 2026-07-02
# forks: help=0, dry-run=0, run=build:1 preflight:1 target:1-each compare:1 baseline-report:1 summary:1 failure-teardown:1

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
benchmark_project="$repo_root/src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj"
solution="$repo_root/PNet.Mesh.sln"

output_dir="$repo_root/artifacts/benchmarks/tun-comparison/latest"
name="pnet-tun-bench"
image="localhost/pnet-mesh-tun:dev"
ping_count=1
warmup="2s"
iperf_duration="3s"
mtu=1280
payload_mode="control"
command_timeout="30s"
build_timeout="180s"
run_timeout="420s"
teardown_timeout="120s"
target="all"
baseline_path=""
build_release=true
preflight=true
dry_run=false
cleanup_enabled=false
cleanup_reason=""

usage() {
  cat <<'USAGE'
Usage:
  scripts/bench-tun-comparison.sh [options]

Runs the PNet.Mesh.Tun and wireguard-go privileged TUN benchmark scenarios, saves
raw scenario JSON, and emits one normalized comparison JSON.

Options:
  --output-dir PATH        Artifact directory. Default: artifacts/benchmarks/tun-comparison/latest.
  --name NAME              Docker topology name. Default: pnet-tun-bench.
  --image IMAGE            TUN benchmark image. Default: localhost/pnet-mesh-tun:dev.
  --ping-count N           Ping count per protocol. Default: 1.
  --warmup DURATION        Warmup duration passed to --tun-benchmark. Default: 2s.
  --iperf-duration DUR     iperf3 duration passed to --tun-benchmark. Default: 3s.
  --mtu BYTES              TUN MTU passed to --tun-benchmark. Default: 1280.
  --payload-mode MODE      iperf profile: control or mtu. Default: control.
  --timeout DURATION       Per-command timeout passed to benchmark internals. Default: 30s.
  --build-timeout DUR      Wrapper timeout for dotnet build. Default: 180s.
  --run-timeout DUR        Wrapper timeout for preflight/benchmark/compare. Default: 420s.
  --target TARGET          all, pnet-mesh-tun, or wireguard-go. Default: all.
  --baseline PATH          Optional previous comparison.json for report-only regression output.
  --no-build               Skip Release build before running.
  --skip-preflight         Skip topology preflight.
  --dry-run                Write commands without running them.
  -h, --help               Show this help.
USAGE
}

log() {
  printf 'level=info tag=tun-bench msg=%q\n' "$*" >&2
}

die() {
  printf 'level=error tag=tun-bench msg=%q\n' "$*" >&2
  exit 2
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"
}

quote_command() {
  local first=true
  local arg
  for arg in "$@"; do
    if [[ "$first" == true ]]; then
      first=false
    else
      printf ' '
    fi
    printf '%q' "$arg"
  done
}

record_command() {
  local label="$1"
  shift
  printf '[%s] %s\n' "$label" "$(quote_command "$@")" >>"$output_dir/commands.log"
}

run_capture() {
  local label="$1"
  local stdout_path="$2"
  local stderr_path="$3"
  local limit="$4"
  shift 4

  record_command "$label" timeout "$limit" "$@"
  if [[ "$dry_run" == true ]]; then
    return 0
  fi

  log "running $label"
  timeout "$limit" "$@" >"$stdout_path" 2>"$stderr_path"
}

write_environment() {
  jq -n \
    --arg name "$name" \
    --arg image "$image" \
    --arg target "$target" \
    --arg pingCount "$ping_count" \
    --arg warmup "$warmup" \
    --arg iperfDuration "$iperf_duration" \
    --arg mtu "$mtu" \
    --arg payloadMode "$payload_mode" \
    --arg commandTimeout "$command_timeout" \
    --arg baseline "$baseline_path" \
    --arg outputDir "$output_dir" \
    '{
      kind: "pnet-mesh-tun-comparison-run",
      createdAt: (now | todateiso8601),
      topology: { name: $name, image: $image },
      target: $target,
      settings: {
        pingCount: ($pingCount | tonumber),
        warmup: $warmup,
        iperfDuration: $iperfDuration,
        mtu: ($mtu | tonumber),
        payloadMode: $payloadMode,
        commandTimeout: $commandTimeout
      },
      baseline: (if $baseline == "" then null else $baseline end),
      outputDir: $outputDir
    }' >"$output_dir/environment.json"
}

write_summary() {
  local status="$1"
  local message="$2"
  jq -n \
    --arg status "$status" \
    --arg message "$message" \
    --arg outputDir "$output_dir" \
    --arg preflight "$output_dir/preflight.json" \
    --arg pnet "$output_dir/pnet-mesh-tun.json" \
    --arg wireguard "$output_dir/wireguard-go.json" \
    --arg comparison "$output_dir/comparison.json" \
    --arg regression "$output_dir/regression-report.json" \
    --arg environment "$output_dir/environment.json" \
    '{
      kind: "pnet-mesh-tun-comparison-script-summary",
      status: $status,
      message: $message,
      outputDir: $outputDir,
      artifacts: {
        preflight: $preflight,
        pnetMeshTun: $pnet,
        wireguardGo: $wireguard,
        comparison: $comparison,
        regression: $regression,
        environment: $environment
      }
    }' >"$output_dir/summary.json"
}

write_regression_report() {
  local current="$output_dir/comparison.json"
  jq -n \
    --arg baselinePath "$baseline_path" \
    --arg currentPath "$current" \
    --slurpfile baseline "$baseline_path" \
    --slurpfile current "$current" '
      def numeric($value): if ($value | type) == "number" then $value else null end;
      def delta($current; $baseline):
        if numeric($current) != null and numeric($baseline) != null
        then $current - $baseline
        else null
        end;
      def delta_percent($current; $baseline):
        if numeric($current) != null and numeric($baseline) != null and $baseline != 0
        then (($current - $baseline) * 100 / $baseline)
        else null
        end;
      def metric($name; $unit; $direction; $current; $baseline): {
        name: $name,
        unit: $unit,
        direction: $direction,
        current: $current,
        baseline: $baseline,
        delta: delta($current; $baseline),
        deltaPercent: delta_percent($current; $baseline)
      };

      ($current[0]) as $c |
      ($baseline[0]) as $b |
      {
        kind: "pnet-mesh-tun-regression-report",
        status: "report-only",
        createdAt: (now | todateiso8601),
        current: $currentPath,
        baseline: $baselinePath,
        note: "Report-only comparison; thresholds are not blocking until repeated runs establish stable variance.",
        managedRuntime: {
          pnet: {
            current: $c.metrics.managedRuntime.pnet,
            baseline: $b.metrics.managedRuntime.pnet
          },
          wireguard: {
            current: $c.metrics.managedRuntime.wireguard,
            baseline: $b.metrics.managedRuntime.wireguard
          }
        },
        metrics: [
          metric("pnet.ipv4.ping.average_latency_ms"; "ms"; "lower"; $c.metrics.traffic.ipv4PingAverageLatencyMilliseconds.pnet; $b.metrics.traffic.ipv4PingAverageLatencyMilliseconds.pnet),
          metric("wireguard.ipv4.ping.average_latency_ms"; "ms"; "lower"; $c.metrics.traffic.ipv4PingAverageLatencyMilliseconds.wireguard; $b.metrics.traffic.ipv4PingAverageLatencyMilliseconds.wireguard),
          metric("pnet.ipv6.ping.average_latency_ms"; "ms"; "lower"; $c.metrics.traffic.ipv6PingAverageLatencyMilliseconds.pnet; $b.metrics.traffic.ipv6PingAverageLatencyMilliseconds.pnet),
          metric("wireguard.ipv6.ping.average_latency_ms"; "ms"; "lower"; $c.metrics.traffic.ipv6PingAverageLatencyMilliseconds.wireguard; $b.metrics.traffic.ipv6PingAverageLatencyMilliseconds.wireguard),
          metric("pnet.ipv4.iperf.bits_per_second"; "bps"; "higher"; $c.metrics.traffic.ipv4IperfBitsPerSecond.pnet; $b.metrics.traffic.ipv4IperfBitsPerSecond.pnet),
          metric("wireguard.ipv4.iperf.bits_per_second"; "bps"; "higher"; $c.metrics.traffic.ipv4IperfBitsPerSecond.wireguard; $b.metrics.traffic.ipv4IperfBitsPerSecond.wireguard),
          metric("pnet.ipv6.iperf.bits_per_second"; "bps"; "higher"; $c.metrics.traffic.ipv6IperfBitsPerSecond.pnet; $b.metrics.traffic.ipv6IperfBitsPerSecond.pnet),
          metric("wireguard.ipv6.iperf.bits_per_second"; "bps"; "higher"; $c.metrics.traffic.ipv6IperfBitsPerSecond.wireguard; $b.metrics.traffic.ipv6IperfBitsPerSecond.wireguard),
          metric("pnet.process.rss_bytes"; "bytes"; "lower"; $c.metrics.process.residentSetBytes.pnet; $b.metrics.process.residentSetBytes.pnet),
          metric("wireguard.process.rss_bytes"; "bytes"; "lower"; $c.metrics.process.residentSetBytes.wireguard; $b.metrics.process.residentSetBytes.wireguard),
          metric("pnet.process.total_cpu_ticks"; "ticks"; "lower"; $c.metrics.process.totalCpuTicks.pnet; $b.metrics.process.totalCpuTicks.pnet),
          metric("wireguard.process.total_cpu_ticks"; "ticks"; "lower"; $c.metrics.process.totalCpuTicks.wireguard; $b.metrics.process.totalCpuTicks.wireguard),
          metric("pnet.managed.allocation_bytes"; "bytes"; "lower"; $c.metrics.managedRuntime.pnet.allocationBytes; $b.metrics.managedRuntime.pnet.allocationBytes),
          metric("pnet.managed.heap_bytes"; "bytes"; "lower"; $c.metrics.managedRuntime.pnet.managedHeapBytes; $b.metrics.managedRuntime.pnet.managedHeapBytes),
          metric("pnet.managed.gen0_collections"; "collections"; "lower"; $c.metrics.managedRuntime.pnet.gen0Collections; $b.metrics.managedRuntime.pnet.gen0Collections),
          metric("pnet.managed.gen1_collections"; "collections"; "lower"; $c.metrics.managedRuntime.pnet.gen1Collections; $b.metrics.managedRuntime.pnet.gen1Collections),
          metric("pnet.managed.gen2_collections"; "collections"; "lower"; $c.metrics.managedRuntime.pnet.gen2Collections; $b.metrics.managedRuntime.pnet.gen2Collections)
        ]
      }' >"$output_dir/regression-report.json"
}

best_effort_teardown() {
  if [[ "$cleanup_enabled" == true && "$dry_run" == false ]]; then
    timeout "$teardown_timeout" dotnet run --project "$benchmark_project" -c Release --no-build -- \
      --tun-topology teardown \
      --name "$name" \
      --image "$image" \
      --timeout "$command_timeout" \
      >"$output_dir/teardown.json" 2>"$output_dir/teardown.err" || true
  fi
}

cleanup_and_exit() {
  local rc="$1"
  cleanup_reason="$2"
  if [[ -n "$cleanup_reason" ]]; then
    log "$cleanup_reason; running best-effort teardown"
  fi
  best_effort_teardown
  exit "$rc"
}

trap 'cleanup_and_exit 130 "interrupted"' INT
trap 'cleanup_and_exit 143 "terminated"' TERM

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output-dir)
      [[ $# -ge 2 ]] || die "--output-dir requires a value"
      output_dir="$2"
      shift 2
      ;;
    --name)
      [[ $# -ge 2 ]] || die "--name requires a value"
      name="$2"
      shift 2
      ;;
    --image)
      [[ $# -ge 2 ]] || die "--image requires a value"
      image="$2"
      shift 2
      ;;
    --ping-count)
      [[ $# -ge 2 && "$2" =~ ^[0-9]+$ && "$2" -gt 0 ]] || die "--ping-count requires a positive integer"
      ping_count="$2"
      shift 2
      ;;
    --warmup)
      [[ $# -ge 2 ]] || die "--warmup requires a value"
      warmup="$2"
      shift 2
      ;;
    --iperf-duration)
      [[ $# -ge 2 ]] || die "--iperf-duration requires a value"
      iperf_duration="$2"
      shift 2
      ;;
    --mtu)
      [[ $# -ge 2 && "$2" =~ ^[0-9]+$ && "$2" -gt 0 ]] || die "--mtu requires a positive integer"
      mtu="$2"
      shift 2
      ;;
    --payload-mode)
      [[ $# -ge 2 ]] || die "--payload-mode requires a value"
      payload_mode="$2"
      [[ "$payload_mode" == "control" || "$payload_mode" == "mtu" ]] || die "--payload-mode must be control or mtu"
      shift 2
      ;;
    --timeout)
      [[ $# -ge 2 ]] || die "--timeout requires a value"
      command_timeout="$2"
      shift 2
      ;;
    --build-timeout)
      [[ $# -ge 2 ]] || die "--build-timeout requires a value"
      build_timeout="$2"
      shift 2
      ;;
    --run-timeout)
      [[ $# -ge 2 ]] || die "--run-timeout requires a value"
      run_timeout="$2"
      shift 2
      ;;
    --target)
      [[ $# -ge 2 ]] || die "--target requires a value"
      target="$2"
      [[ "$target" == "all" || "$target" == "pnet-mesh-tun" || "$target" == "wireguard-go" ]] || die "--target must be all, pnet-mesh-tun, or wireguard-go"
      shift 2
      ;;
    --baseline)
      [[ $# -ge 2 ]] || die "--baseline requires a value"
      baseline_path="$2"
      shift 2
      ;;
    --no-build)
      build_release=false
      shift
      ;;
    --skip-preflight)
      preflight=false
      shift
      ;;
    --dry-run)
      dry_run=true
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

require_command dotnet
require_command jq
require_command timeout

if [[ -n "$baseline_path" && ! -f "$baseline_path" ]]; then
  die "baseline comparison JSON not found: $baseline_path"
fi

mkdir -p "$output_dir"
: >"$output_dir/commands.log"
write_environment
cleanup_enabled=true

if [[ "$build_release" == true ]]; then
  if ! run_capture build "$output_dir/build.log" "$output_dir/build.err" "$build_timeout" \
    dotnet build "$solution" -c Release --no-restore; then
    write_summary fail "Release build failed; see build.log and build.err."
    exit 1
  fi
fi

if [[ "$preflight" == true ]]; then
  if ! run_capture preflight "$output_dir/preflight.json" "$output_dir/preflight.err" "$run_timeout" \
    dotnet run --project "$benchmark_project" -c Release --no-build -- \
      --tun-topology preflight \
      --name "$name" \
      --image "$image" \
      --timeout "$command_timeout"; then
    write_summary skip "Topology preflight command failed; see preflight.err."
    exit 0
  fi

  if [[ "$dry_run" == false ]]; then
    preflight_status="$(jq -r '.status // "fail"' "$output_dir/preflight.json")"
    if [[ "$preflight_status" != "pass" ]]; then
      write_summary skip "Topology preflight returned $preflight_status; see preflight.json."
      exit 0
    fi
  fi
fi

run_target() {
  local scenario="$1"
  run_capture "$scenario" "$output_dir/$scenario.json" "$output_dir/$scenario.err" "$run_timeout" \
    dotnet run --project "$benchmark_project" -c Release --no-build -- \
      --tun-benchmark "$scenario" \
      --name "$name" \
      --image "$image" \
      --ping-count "$ping_count" \
      --warmup "$warmup" \
      --iperf-duration "$iperf_duration" \
      --timeout "$command_timeout" \
      --mtu "$mtu" \
      --payload-mode "$payload_mode"
}

status="pass"
message="TUN comparison benchmark completed."

if [[ "$target" == "all" || "$target" == "pnet-mesh-tun" ]]; then
  if ! run_target pnet-mesh-tun; then
    best_effort_teardown
    status="fail"
    message="PNet.Mesh.Tun benchmark failed; see pnet-mesh-tun.json and pnet-mesh-tun.err."
  fi
fi

if [[ "$target" == "all" || "$target" == "wireguard-go" ]]; then
  if ! run_target wireguard-go; then
    best_effort_teardown
    status="fail"
    message="wireguard-go benchmark failed; see wireguard-go.json and wireguard-go.err."
  fi
fi

if [[ "$dry_run" == true ]]; then
  if [[ "$target" == "all" ]]; then
    run_capture compare "$output_dir/comparison.json" "$output_dir/comparison.err" "$run_timeout" \
      dotnet run --project "$benchmark_project" -c Release --no-build -- \
        --tun-compare \
        --pnet "$output_dir/pnet-mesh-tun.json" \
        --wireguard "$output_dir/wireguard-go.json"
    if [[ -n "$baseline_path" ]]; then
      record_command baseline-report jq "$output_dir/comparison.json" "$baseline_path" '>' "$output_dir/regression-report.json"
    fi
  fi
  write_summary pass "Dry run completed; commands.log lists the commands that would run."
  exit 0
fi

if [[ "$target" == "all" && -s "$output_dir/pnet-mesh-tun.json" && -s "$output_dir/wireguard-go.json" ]]; then
  compare_rc=0
  run_capture compare "$output_dir/comparison.json" "$output_dir/comparison.err" "$run_timeout" \
    dotnet run --project "$benchmark_project" -c Release --no-build -- \
      --tun-compare \
      --pnet "$output_dir/pnet-mesh-tun.json" \
      --wireguard "$output_dir/wireguard-go.json" || compare_rc=$?
  if [[ "$compare_rc" -ne 0 ]]; then
    status="fail"
    if [[ -s "$output_dir/comparison.json" ]]; then
      message="Comparison generated with failing benchmark inputs; see comparison.json."
    else
      message="Comparison generation failed; see comparison.json and comparison.err."
    fi
  fi

  if [[ -n "$baseline_path" && -s "$output_dir/comparison.json" ]]; then
    if ! write_regression_report; then
      status="fail"
      message="Report-only baseline comparison failed; see regression-report.json."
    fi
  fi
elif [[ "$target" != "all" ]]; then
  message="Target filter '$target' completed; comparison.json was not generated because both targets are required."
else
  status="fail"
  message="Comparison skipped because one or both target JSON files are missing."
fi

write_summary "$status" "$message"
[[ "$status" == "pass" ]]

# --- Testing ---
# Inputs: CLI options above, optional --baseline comparison.json, dotnet SDK, jq, timeout, Docker/TUN host for non-dry runs.
# Key functions: run_capture wraps timed commands, write_environment/write_summary emit JSON artifacts, write_regression_report emits report-only latency/bandwidth/CPU/RSS/GC/allocation deltas plus managed-counter availability, run_target invokes each scenario, cleanup handles failure/signal teardown.
# Edge cases: missing dependency, missing baseline, preflight skip/fail, one target filtered, benchmark failure, interrupted run, custom output dir/name/image/MTU/payload mode.
# Fixtures: --dry-run --target all, --target pnet-mesh-tun, --payload-mode mtu --mtu 1420, --baseline /tmp/baseline-comparison.json, preflight skip on host without /dev/net/tun.
# Run: bash -n scripts/bench-tun-comparison.sh && scripts/bench-tun-comparison.sh --help && scripts/bench-tun-comparison.sh --dry-run --output-dir /tmp/pnet-tun-compare-dry-run
