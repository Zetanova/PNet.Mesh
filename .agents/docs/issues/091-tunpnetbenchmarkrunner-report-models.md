---
issue: 091
date: 2026-07-03
source: refactor-audit
priority: low
status: completed
terminal-state: completed
research-status: complete
research-date: 2026-07-03
assumptions-date: 2026-07-03
completed-date: 2026-07-03
completed-commits:
  - 4cb0090c463832e9b435d141830514303a641ced
split-status: child
parent-issue: 086
brief: "description+scope+acceptance-criteria+assumptions"
views:
  fix: "description+parent-tracking+scope+acceptance-criteria+assumptions"
  enrich: "description+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report+resolving-commits"
---

# 091 - Extract TUN Benchmark Report Models

## Description

Split the benchmark report records and report assembly helpers out of `TunPNetBenchmarkRunner.cs`.

## Parent Tracking

- Parent: #086
- Extracted scope: move the benchmark report records and the report assembly helpers into dedicated files.
- Standalone reason: these records are consumed by the comparison runner and tests and should live outside the orchestration file.

## Scope

- In scope: `TunPNetBenchmarkReport`, `TunBenchmarkImplementationInfo`, `TunPNetBenchmarkSettings`, `TunBenchmarkTrafficResult`, `TunBenchmarkProcessMetrics`, `TunBenchmarkManagedRuntimeMetrics`, and the report assembly helpers that construct them.
- Out of scope: CLI parsing, traffic execution, process metrics collection, and topology orchestration.

## Acceptance Criteria

- Report record declarations move out of the runner file without changing property names.
- Comparison runner and unit test consumers keep compiling against the same shapes.
- JSON output remains stable.
- Build and benchmark-related tests pass.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The report record declarations are clustered at the tail of `TunPNetBenchmarkRunner.cs`. | verified | source | `src/PNet.Mesh.Benchmarks/TunPNetBenchmarkRunner.cs:1611-1688`. |
| 2 | F | `TunBenchmarkComparisonRunner` and its tests consume the report record types. | verified | source | `src/PNet.Mesh.Benchmarks/TunBenchmarkComparisonRunner.cs` and `src/PNet.Mesh.Tun.UnitTests/TunBenchmarkComparisonRunnerTests.cs` reference the report records. |

## Completion Report

Resolved in `4cb0090c463832e9b435d141830514303a641ced`.

## Resolving Commits

- `4cb0090c463832e9b435d141830514303a641ced`
