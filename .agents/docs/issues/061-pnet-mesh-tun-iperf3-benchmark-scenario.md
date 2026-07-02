---
issue: 061
date: 2026-07-02
source: benchmark/integration-phase-2
priority: medium
status: gated
terminal-state: gated
gate-depends: [060]
gate-reason: "Requires the privileged benchmark topology from #060."
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

This issue stays gated until #060 defines the privileged benchmark topology. The #054 benchmark foundation, #056 macro harnesses, and #059 optional TUN component are complete; preserve the #059 smoke evidence as the baseline connectivity precondition.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The user wants TUN-enabled PNet.Mesh to be benchmarked with normal tools such as `iperf3`. | verified | source | The user asked whether TUN can be added so regular tools like `iperf3` can test PNet.Mesh. |
| 2 | F | #059 defines `PNet.Mesh.Tun` as optional and benchmark-enabling. | verified | source | #059 playbook calls out the benchmark bridge for `ping` and `iperf3`. |
| 3 | F | Allocation data is required in the benchmark rollout. | verified | source | #054-#058 include allocation metrics and the user explicitly asked that allocations be measured. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-02 | `gate-depends: [056, 059, 060]` | source | partially passed | #056 and #059 are complete; #060 remains open, so #061 stays gated only on #060. |

## Validation History

- 2026-07-02: dependency gates cleared by #054, #056, and #059; #060 keeps #061 gated.
