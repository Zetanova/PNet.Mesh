---
issue: 063
date: 2026-07-02
source: benchmark/integration-phase-4
priority: medium
status: gated
terminal-state: gated
gate-depends: [057, 061, 062]
gate-reason: "Requires baseline policy and both TUN benchmark scenarios before comparison schema can be finalized."
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

# 063 - TUN Comparison Result Schema And Allocation Counters

## Description

Define and implement the result schema for PNet.Mesh.Tun versus `wireguard-go` benchmark comparison. The schema should preserve raw tool output while normalizing latency, bandwidth, CPU, memory, GC, and allocation metrics for reporting and later regression analysis.

## Playbook

- `Raw plus normalized`: store original `ping` and `iperf3` output plus normalized fields used by reports.
- `Comparable metrics`: normalize latency, bandwidth, loss, CPU, RSS, duration, MTU, payload profile, and environment metadata.
- `PNet-specific metrics`: record .NET GC and allocation counters for PNet.Mesh runs without forcing equivalent fields onto `wireguard-go`.
- `Traceability`: include git commit, executable versions, command line, topology id, and run timestamp.

## Scope

- Define a JSON or similarly structured result schema for TUN integration benchmark runs.
- Add parser/export logic for PNet.Mesh.Tun benchmark output.
- Add parser/export logic for `wireguard-go` benchmark output.
- Include PNet.Mesh process allocations, GC collections, managed heap size, RSS, CPU time, and runtime metadata.
- Include comparison report fields that show side-by-side latency and bandwidth without hiding raw measurements.

## Out Of Scope

- Running benchmark jobs on a schedule.
- Setting hard performance failure thresholds.
- Optimizing measured performance gaps.

## Acceptance Criteria

- A single result schema can represent PNet.Mesh.Tun and `wireguard-go` benchmark runs.
- PNet.Mesh output includes allocation and GC metrics when counters are available.
- `wireguard-go` output includes process CPU/RSS and clearly omits .NET-only allocation counters.
- Comparison reports can be generated from saved result files without rerunning benchmarks.

## Gate

This issue stays gated until #057 defines the baseline policy and #061/#062 produce both benchmark scenarios.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Benchmark reports need allocation and GC data for PNet.Mesh. | verified | source | #054-#058 require allocation metrics and baseline policy coverage. |
| 2 | R | Raw output should be retained next to normalized fields. | verified | logical | Retaining raw tool output makes parser changes auditable and avoids losing data needed for reanalysis. |
| 3 | R | `wireguard-go` comparison should use process metrics instead of .NET counters. | verified | logical | .NET GC and allocation counters only describe the managed PNet.Mesh process. |
