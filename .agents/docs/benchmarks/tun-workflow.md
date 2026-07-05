---
title: TUN Benchmark Workflow
assumptions-date: 2026-07-02
status: report-only
brief: "quick-start+artifact-layout+scheduled-runner+promotion"
---

# TUN Benchmark Workflow

Manual-first workflow for privileged PNet.Mesh.Tun versus `wireguard-go` benchmark runs.

## Quick Start

Run on a Linux host or runner with Docker, `/dev/net/tun`, `CAP_NET_ADMIN`, and `CAP_NET_RAW` support:

```bash
docker build -f src/PNet.Mesh.Tun.Cli/Dockerfile --build-arg PNET_MESH_DEFINE_CONSTANTS=PNET_MESH_PACKET_TRACE -t localhost/pnet-mesh-tun:dev .
timeout 900s scripts/bench-tun-comparison.sh --output-dir artifacts/benchmarks/tun-comparison/latest --baseline artifacts/benchmarks/tun-comparison/baseline/comparison.json
dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --udp-socket-probe --mode all --iterations 1000 --warmup 100
```

Omit `--baseline` for the first run. Keep the whole output directory as the archived run artifact.

## Artifact Layout

| Path | Purpose |
|---|---|
| `environment.json` | Runner, topology, workload, baseline path, and output directory metadata. |
| `commands.log` | Exact timed commands executed by the wrapper. |
| `build.log`, `build.err` | Release build output when build is enabled. |
| `preflight.json`, `preflight.err` | Non-mutating privileged-runner capability check. |
| `pnet-mesh-tun.json`, `pnet-mesh-tun.err` | Raw PNet.Mesh.Tun benchmark report and stderr. |
| `wireguard-go.json`, `wireguard-go.err` | Raw `wireguard-go` benchmark report and stderr. |
| `comparison.json`, `comparison.err` | Normalized latency, bandwidth, CPU, single-process RSS/HWM, GC, allocation, environment, and raw-output comparison. |
| `<name>-pnet-mesh-tun-<role>-packet-trace.csv` | Optional PNet.Mesh.Tun packet trace copied when `--trace-output-dir <dir>` is set. |
| `regression-report.json` | Optional report-only deltas versus `--baseline`. |
| `summary.json` | Wrapper status, message, and artifact paths. |
| `teardown.json`, `teardown.err` | Failure or signal cleanup evidence when teardown runs. |

## Manual Workflow

1. Restore and build Release, or let the wrapper build with its default `--build-timeout`.
2. Build `localhost/pnet-mesh-tun:dev` from `src/PNet.Mesh.Tun.Cli/Dockerfile`; include `--build-arg PNET_MESH_DEFINE_CONSTANTS=PNET_MESH_PACKET_TRACE` when packet traces are needed.
3. Run `scripts/bench-tun-comparison.sh` with a timestamped `--output-dir`.
4. Treat the unmeasured one-packet readiness ping as setup: a 3s timeout or loss means the measured ping/iperf phase must not start.
5. Treat `summary.json` status `pass` as a completed measurement run, `skip` as inconclusive environment evidence, and `fail` as an actionable run failure.
6. Promote a successful run by copying its `comparison.json` into the baseline location only when the benchmark code, runtime, host class, or workload intentionally changes.

## Probe Flags

| Flag | Purpose |
|---|---|
| `--udp-socket-probe` | Runs loopback UDP receive API probes without Docker or TUN privileges. |
| `--trace-output-dir <dir>` | Captures PNet.Mesh.Tun packet-trace CSV files for `pnet-mesh-tun` runs. |
| `--pnet-udp-receive-mode async|blocking` | Forces the PNet.Mesh.Tun UDP receive path for benchmark probes. |
| `--pnet-udp-socket-buffer-bytes <bytes>` | Sets PNet.Mesh.Tun UDP receive/send socket buffer sizes for benchmark probes. |

## Scheduled Runner

Use scheduling only on dedicated privileged Linux runners:

| Requirement | Reason |
|---|---|
| Docker or compatible engine can run privileged containers. | The benchmark creates isolated TUN namespaces. |
| `/dev/net/tun` is available to containers. | Both implementations need TUN interfaces. |
| `CAP_NET_ADMIN` and `CAP_NET_RAW` are permitted. | Setup requires interface and ping operations. |
| Runner image includes Docker CLI, .NET SDK 10, `jq`, `timeout`, `ping`, `iperf3`, UDP `nc`, and `wireguard-go`. | The wrapper, topology, and traffic probes depend on these tools. |
| Artifact collection preserves the full output directory. | Regression review needs raw outputs, normalized summaries, logs, and metadata. |

Scheduled runs must not fail the pipeline solely because preflight returns `skip`; publish the artifacts and mark the job inconclusive. A wrapper `fail` should fail the scheduled job because it means the runner was capable enough to start but the benchmark or comparison failed.

## Baseline Reporting

`--baseline <comparison.json>` writes `regression-report.json` with `status: "report-only"`. The report covers:

- IPv4 and IPv6 ping average latency.
- IPv4 and IPv6 `iperf3` bandwidth.
- Single representative-process RSS/HWM and process CPU ticks for both implementations.
- PNet.Mesh.Tun managed allocation bytes, managed heap bytes, and Gen0/Gen1/Gen2 collections where counters are available.

The report also preserves current and baseline managed-runtime availability, unavailable, and not-applicable reasons so null allocation or GC values remain reviewable.

Do not gate commits on these deltas until the promotion criteria below are met. File optimization issues only when repeated comparable runs point to the same regression, or when one run shows an obvious large regression with no workload or host explanation.

## Promotion

Move any TUN metric from report-only to blocking only after all conditions hold:

- At least three successful runs exist for the same runner class, runtime, image, MTU, payload mode, ping count, and `iperf3` duration.
- Normal host variance is smaller than the proposed threshold.
- The job distinguishes `skip`, `fail`, and regression failure in its final status.
- The threshold protects a hot path already backed by measured issue or production evidence.
- Failure output names the metric, baseline, current value, delta, percent delta, and run artifact directory.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The wrapper can run both TUN benchmark implementations and emit comparison artifacts. | verified | source | `scripts/bench-tun-comparison.sh` orchestrates build, preflight, both targets, `--tun-compare`, optional `--baseline`, and summary output. |
| 2 | F | Unsupported privileged hosts must be reported as inconclusive instead of success. | verified | source | `--tun-topology preflight` returns `pass`, `skip`, or `fail`; the wrapper maps non-pass preflight to summary status `skip`. |
| 3 | R | TUN benchmark thresholds should remain report-only until repeated runner-class variance is known. | verified | logical | The workflow depends on privileged containers and host networking, so one run cannot prove stable variance. |
