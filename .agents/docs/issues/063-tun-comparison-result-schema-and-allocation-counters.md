---
issue: 063
date: 2026-07-02
source: benchmark/integration-phase-4
priority: medium
status: completed
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - 9bd8a8ad393c4f4f34a9816b29c3b698f0a66c66
gate-depends: [057, 061, 062]
gate-reason: "Requires baseline policy and both TUN benchmark scenarios before comparison schema can be finalized."
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

Cleared on 2026-07-02: #057, #061, and #062 are complete, so #063 was ready.

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-02 | `gate-depends: [057, 061, 062]` | source | ready | #057 and #061 are complete; #062 completed in `efecd4a`, so #063 is ready. |

## Validation History

- 2026-07-02: dependency gates cleared by #057, #061, and #062; #063 was ready before completion.

## Completion Report

Implemented in `9bd8a8ad393c4f4f34a9816b29c3b698f0a66c66`.

- Added the saved-result `--tun-compare` CLI path for PNet.Mesh.Tun versus `wireguard-go` benchmark reports.
- Normalized side-by-side latency, bandwidth, packet loss, CPU, RSS, thread, settings, environment, implementation, traceability, and managed-runtime fields while preserving raw traffic, process, topology, and command records.
- Added tests covering the public CLI route, real saved-report serialization, and wrong-scenario validation.
- Kept the comparison output usable from saved benchmark files so reports can be generated without rerunning benchmarks.

Verification on 2026-07-02:

- `timeout 180s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` passed with 9 projects and 0 warnings.
- `timeout 240s rtk dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none` passed, 14/14 tests.
- `timeout 120s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-compare --pnet /tmp/codex-team-task.krDAde/test/tun-benchmark-pnet-closeout-seq.json --wireguard /tmp/codex-team-task.krDAde/test/tun-benchmark-wireguard-go-closeout.json > /tmp/codex-team-task.krDAde/test/tun-compare-063-final.json` passed.
- `timeout 120s rtk dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh.Benchmarks/Program.cs src/PNet.Mesh.Benchmarks/BenchmarkCli.cs src/PNet.Mesh.Benchmarks/TunPNetBenchmarkRunner.cs src/PNet.Mesh.Benchmarks/TunBenchmarkComparisonRunner.cs src/PNet.Mesh.Tun.UnitTests/TunBenchmarkComparisonRunnerTests.cs --no-restore --verify-no-changes --verbosity minimal` passed.
- `git diff --check` passed.

## Resolving Commits

- `9bd8a8ad393c4f4f34a9816b29c3b698f0a66c66` - `benchmarks: add tun comparison result schema`

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Benchmark reports need allocation and GC data for PNet.Mesh. | verified | source | #054-#058 require allocation metrics and baseline policy coverage. |
| 2 | R | Raw output should be retained next to normalized fields. | verified | logical | Retaining raw tool output makes parser changes auditable and avoids losing data needed for reanalysis. |
| 3 | R | `wireguard-go` comparison should use process metrics instead of .NET counters. | verified | logical | .NET GC and allocation counters only describe the managed PNet.Mesh process. |
