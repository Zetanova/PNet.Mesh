---
issue: 065
date: 2026-07-02
source: benchmark/integration-script
priority: medium
status: ready
terminal-state: ready
gate-depends: [063]
gate-reason: "Requires the comparison result schema before the runner script can be implemented safely."
gate-last-checked: 2026-07-02
gate-status: cleared
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

# 065 - TUN Comparison Benchmark Runner Script

## Description

Create a single-command script that runs the PNet.Mesh.Tun and `wireguard-go` TUN benchmark comparison for latency, bandwidth, and Memory/RSS. The script should orchestrate setup, preflight, both benchmark targets, result capture, and teardown using the topology and result schema from the integration benchmark issues.

## Playbook

- `Entrypoint`: prefer `scripts/bench-tun-comparison.sh` unless implementation finds a better existing project convention.
- `Targets`: run PNet.Mesh.Tun and `wireguard-go` with the same topology, MTU, addresses, duration, and traffic profile.
- `Metrics`: collect latency, bandwidth, Memory/RSS, CPU, and PNet.Mesh-specific .NET GC/allocation counters when available.
- `Safety`: use preflight checks, explicit skip/inconclusive output, signal traps, and teardown so privileged network state is cleaned up.
- `Artifacts`: write raw tool output, normalized result files, logs, and environment metadata to a documented output directory.

## Scope

- Add a single script entrypoint for the full PNet.Mesh.Tun versus `wireguard-go` benchmark comparison.
- Accept configurable output directory, duration, warmup, MTU, payload mode, and target selection options.
- Run IPv4 and IPv6 latency probes for both targets.
- Run IPv4 and IPv6 bandwidth probes for both targets.
- Capture Memory/RSS for PNet.Mesh.Tun and `wireguard-go` processes.
- Capture PNet.Mesh .NET GC/allocation counters when available and clearly mark them as PNet-specific.
- Emit output compatible with the comparison schema from #063.
- Follow the project script rules for optimization metadata, signal handling, command timeouts, and testing footer.

## Out Of Scope

- Implementing the TUN component.
- Defining the benchmark topology.
- Creating the result schema.
- Enforcing performance regression thresholds.

## Acceptance Criteria

- A documented single-command script runs the PNet.Mesh.Tun and `wireguard-go` comparison benchmark or reports an explicit skip/inconclusive reason.
- The script produces latency, bandwidth, Memory/RSS, CPU, environment metadata, and PNet.Mesh-specific allocation/GC artifacts.
- The script runs both IPv4 and IPv6 traffic profiles unless explicitly filtered by an option.
- The script uses deterministic output paths and writes raw plus normalized result files.
- The script has required script optimization header metadata, signal cleanup, command timeouts, and a testing footer.

## Gate

Cleared on 2026-07-02: #063 is complete, so this issue is ready. The privileged topology from #060 is complete in `1e079d2`, #061 completed the PNet.Mesh.Tun benchmark scenario, and #062 completed the `wireguard-go` comparison scenario.

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-02 | `gate-depends: [061, 062, 063]` | source | reduced | #061 and #062 are complete, so #065 now remains gated only on #063. |
| 2026-07-02 | `gate-depends: [061, 062, 063]` | source | ready | #061, #062, and #063 are complete, so #065 is ready. |

## Validation History

- 2026-07-02: dependency gates cleared by #061 and #062; #063 kept #065 gated until completion.
- 2026-07-02: dependency gates cleared by #061, #062, and #063; #065 is now ready.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The user requested an issue for a script that creates the PNet.Mesh and `wireguard-go` benchmark for latency, bandwidth, and Memory/RSS. | verified | source | Current user request asks to "file an issue to create a script" for those measurements. |
| 2 | F | The repository has an existing `scripts/` directory. | verified | source | `scripts/packages.sh` exists. |
| 3 | F | The integration benchmark topology, scenarios, and result schema are already tracked by #060-#063. | verified | source | #060 tracks topology, #061 PNet.Mesh.Tun traffic, #062 `wireguard-go`, and #063 result schema/counters. |
| 4 | F | New shell scripts must include optimization metadata, signal handling, command timeouts, and a testing footer. | verified | source | Project-imported script rules define these requirements for `.sh` scripts. |
