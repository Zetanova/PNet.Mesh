---
issue: 061
date: 2026-07-02
source: benchmark/integration-phase-2
priority: medium
status: completed
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - 53ca5bb
  - d667db3
gate-last-checked: 2026-07-02
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
brief: "description+playbook+scope+acceptance-criteria+gate"
views:
  enrich: "description+playbook+scope+gate+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+gate+assumptions"
  complete: "description+completion-report+resolving-commits"
---

# 061 - PNet.Mesh.Tun iperf3 Benchmark Scenario

## Description

Add the PNet.Mesh.Tun integration benchmark scenario that runs normal network tools over the optional TUN interface. The scenario should exercise IPv4 and IPv6 packet paths with `ping` latency probes and `iperf3` bandwidth runs using the topology from #060.

## Playbook

- `Traffic tools`: use `ping`/`ping6` or equivalent plus `iperf3` so the test follows normal OS networking behavior.
- `Protocol coverage`: run both IPv4 and IPv6 paths through PNet.Mesh.Tun.
- `Warmup`: separate topology startup and tunnel handshake from measured traffic windows.
- `Current PNet.Mesh.Tun benchmark`: the diagnostic runner can exchange IPv4/IPv6 ping and UDP `iperf3` traffic, collect process metrics, and report managed counters as explicitly unavailable when `dotnet-counters` is absent.
- `Output`: emit machine-readable latency, bandwidth, packet-loss, duration, payload, MTU, CPU, RSS, and allocation counters where available.

## Scope

- Add a runnable PNet.Mesh.Tun benchmark command for IPv4 ping latency.
- Add a runnable PNet.Mesh.Tun benchmark command for IPv6 ping latency.
- Add `iperf3` TCP or UDP throughput runs for IPv4 and IPv6, with fixed duration and parallelism.
- Capture PNet.Mesh process metrics, .NET GC counters, and allocation counters alongside tool output.
- Document manual/privileged requirements and expected skip behavior.

## Out Of Scope

- Comparing against `wireguard-go`.
- Setting regression thresholds.
- Optimizing any measured bottleneck.

## Acceptance Criteria

- PNet.Mesh.Tun benchmark runs produce machine-readable results for IPv4 and IPv6 latency.
- PNet.Mesh.Tun benchmark runs produce machine-readable `iperf3` bandwidth results for IPv4 and IPv6.
- Result output includes enough metadata to identify git commit, runtime, topology, MTU, payload profile, and host environment.
- Allocation and GC data are captured for the PNet.Mesh process or explicitly reported as unavailable with a reason.

## Gate

Cleared. The #060 gate is cleared by `1e079d2`, which added the privileged topology plan, preflight, create, and teardown commands. The #071 traffic gate is cleared by `d667db3`, which stabilized IPv4/IPv6 ping and `iperf3` over the OS TUN path.

## Completion Report

Implemented in `53ca5bb` and stabilized in `d667db3`.

- Added the PNet.Mesh.Tun benchmark runner for the privileged topology from #060.
- The runner emits machine-readable IPv4/IPv6 ping latency and UDP `iperf3` bandwidth results.
- The report includes runtime, OS, architecture, Docker engine version, topology, MTU, traffic profile, process RSS/CPU metrics, command records, and managed-counter availability.
- The final #071 verification run returned `status: pass` for IPv4 ping, IPv6 ping, IPv4 `iperf3`, and IPv6 `iperf3`; process metrics were present and managed counters were explicitly unavailable because `dotnet-counters` is not installed in the TUN CLI image.
- Labeled Docker cleanup left no `pnet.mesh.benchmark.topology=pnet-tun-bench` containers or networks after the benchmark.

## Resolving Commits

- `53ca5bb` - `benchmarks: add TUN diagnostic traffic runner`
- `d667db3` - `benchmarks: stabilize PNet.Mesh.Tun traffic`

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The user wants TUN-enabled PNet.Mesh to be benchmarked with normal tools such as `iperf3`. | verified | source | The user asked whether TUN can be added so regular tools like `iperf3` can test PNet.Mesh. |
| 2 | F | #059 defines `PNet.Mesh.Tun` as optional and benchmark-enabling. | verified | source | #059 playbook calls out the benchmark bridge for `ping` and `iperf3`. |
| 3 | F | Allocation data is required in the benchmark rollout. | verified | source | #054-#058 include allocation metrics and the user explicitly asked that allocations be measured. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-02 | `gate-depends: [056, 059, 060]` | source | passed | #056, #059, and #060 are complete; #061 is ready. |
| 2026-07-02 | `gate-depends: [071]` | test | blocked | After rebuilding `localhost/pnet-mesh-tun:dev`, `timeout 180s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-benchmark pnet-mesh-tun --ping-count 1 --warmup 0ms --iperf-duration 1s --timeout 15s` wrote `/tmp/pnet-tun-benchmark.json` with `status: fail`; IPv4 ping received 1/9 packets, IPv6 ping lost 10/10 packets, IPv4 `iperf3` exited 1, IPv6 `iperf3` failed readiness, both TUN processes had `/proc` metrics, teardown passed, and no labeled Docker resources remained. |
| 2026-07-02 | `gate-depends: [071]` | test | passed | `d667db3` stabilized the TUN traffic path. The final `timeout 420s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-benchmark pnet-mesh-tun --ping-count 1 --iperf-duration 3s` run returned `status: pass` with IPv4/IPv6 ping, IPv4/IPv6 `iperf3`, process metrics, explicit managed-counter unavailability, and clean labeled Docker teardown. |

## Validation History

- 2026-07-02: dependency gates cleared by #054, #056, #059, and #060.
- 2026-07-02: traffic gate cleared by #071 completion in `d667db3`; #061 completed.
