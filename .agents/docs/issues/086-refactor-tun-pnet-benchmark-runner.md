---
issue: 086
date: 2026-07-03
source: refactor-audit
priority: low
status: completed
split-status: parent
gate-depends: [089, 090, 091]
gate-reason: "Tracking parent waits for fine-grained child issues"
ungate-when: "All child issues are completed"
terminal-state: completed
research-status: complete
research-date: 2026-07-03
assumptions-date: 2026-07-03
gate-last-checked: 2026-07-03
gate-status: cleared
completed-date: 2026-07-03
completed-commits:
  - 4cb0090c463832e9b435d141830514303a641ced
brief: "description+scope+acceptance-criteria+assumptions"
views:
  fix: "description+scope+acceptance-criteria+proposed-refactor+verification+assumptions"
  enrich: "description+scope+acceptance-criteria+proposed-refactor+verification+assumptions"
  complete: "description+completion-report+resolving-commits"
---

# 086 - Refactor TUN PNet Benchmark Runner

## Description

Source proposal: `RP-004`.

`TunPNetBenchmarkRunner.cs` is 1,688 lines and mixes CLI dispatch, topology orchestration, traffic parsing, option parsing, process metric collection, managed-runtime metric collection, and JSON report records. The report records are also consumed by the comparison runner and TUN unit tests.

This issue now tracks the split child issues below.

## Scope

- In scope: file organization for TUN benchmark runner internals, options, parsers, metrics helpers, and report records.
- Preserve the `--tun-benchmark` CLI surface and JSON report contract.
- Out of scope: benchmark semantics, topology command behavior, container lifecycle, and regression threshold policy.

## Acceptance Criteria

- Runner orchestration, option parsing, traffic parsing, metrics, and report models are split into cohesive files.
- Existing CLI output and JSON property names remain stable.
- TUN benchmark unit tests and comparison tests continue to pass.
- Build remains warning-free.

## Tracking

| Child | Scope | Status | Notes |
|-------|-------|--------|-------|
| #089 | TUN benchmark options and CLI surface | open | Standalone parser/usage extraction |
| #090 | TUN benchmark execution parsers and metrics helpers | open | Standalone execution-helper extraction |
| #091 | TUN benchmark report models and assembly helpers | open | Standalone report-model extraction |

## Residual Scope
none

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-03 | `gate-depends: [089, 090, 091]` | source | completed | #089, #090, and #091 are completed, so the parent tracking issue is complete. |

## Validation History

- 2026-07-03: dependency gate cleared by #089; remaining dependency gates #090 and #091 keep #086 gated.
- 2026-07-03: dependency gate cleared by #090; remaining dependency gate #091 keeps #086 gated.
- 2026-07-03: dependency gate cleared by #091; all child issues are complete, so #086 is completed.

## Proposed Refactor

Split option parsing, ping/iperf parsing, process/managed metric helpers, and report records into focused files under `src/PNet.Mesh.Benchmarks`. Keep `TunPNetBenchmarkRunner` as the orchestration entry point.

## Verification

```bash
dotnet build PNet.Mesh.sln -c Release --no-restore
dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none
dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh.Benchmarks/TunPNetBenchmarkRunner.cs --no-restore --verify-no-changes --verbosity minimal
```

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `TunPNetBenchmarkRunner.cs` is 1,688 lines. | verified | test | `wc -l src/PNet.Mesh.Benchmarks/TunPNetBenchmarkRunner.cs` returned 1,688. |
| 2 | F | The runner file mixes orchestration, traffic parsing, option parsing, and report model declarations. | verified | source | `src/PNet.Mesh.Benchmarks/TunPNetBenchmarkRunner.cs:25-125`, `:128-220`, `:1378-1608`, and `:1611-1688`. |
| 3 | F | TUN benchmark report records are consumed outside the runner file. | verified | source | `src/PNet.Mesh.Benchmarks/TunBenchmarkComparisonRunner.cs` and `src/PNet.Mesh.Tun.UnitTests/TunBenchmarkComparisonRunnerTests.cs` reference `TunPNetBenchmarkReport` and related records. |

## Completion Report

Completed the tracking parent after child issues #089, #090, and #091 landed in `4cb0090c463832e9b435d141830514303a641ced`.

## Resolving Commits

- `4cb0090c463832e9b435d141830514303a641ced`
