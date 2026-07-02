---
issue: 061
date: 2026-07-02
source: benchmark/integration-phase-2
priority: medium
status: gated
terminal-state: gated
gate-depends: [071]
gate-reason: "The diagnostic runner exists, but sustained PNet.Mesh.Tun OS traffic currently fails ping and iperf3; #071 must stabilize traffic before this benchmark can meet acceptance."
gate-last-checked: 2026-07-02
probeable: false
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
- `Current #059 smoke`: component-level Docker smoke can exchange IPv4 ping and IPv4/IPv6 UDP `nc` packets, but repeated ping and `iperf3` are not yet stable enough to use as a pass/fail benchmark gate.
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

The #060 gate is cleared by `1e079d2`, which added the privileged topology plan, preflight, create, and teardown commands. Traffic acceptance is now gated by #071: the current diagnostic runner can start both TUN processes and collect metrics, but IPv4/IPv6 ping and `iperf3` do not pass over the OS TUN path.

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

## Validation History

- 2026-07-02: dependency gates cleared by #054, #056, #059, and #060.
