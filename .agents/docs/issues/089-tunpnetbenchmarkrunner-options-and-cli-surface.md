---
issue: 089
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

# 089 - Extract TUN Benchmark Options and CLI Surface

## Description

Split the `TunPNetBenchmarkOptions` parser and CLI usage helpers out of `TunPNetBenchmarkRunner.cs`.

## Parent Tracking

- Parent: #086
- Extracted scope: move `TunPNetBenchmarkOptions`, usage text, command-line formatting, and parse helpers into a focused file.
- Standalone reason: these helpers define the CLI surface and can move without changing benchmark execution behavior.

## Scope

- In scope: option parsing, usage text, duration/int parsing helpers, and command-line rendering.
- Out of scope: traffic execution, metrics collection, report creation, and topology orchestration.

## Acceptance Criteria

- `--tun-benchmark` parsing still accepts the same options and validations.
- Help text and rendered command-line values remain stable.
- Existing unit tests for option parsing continue to pass.
- Build remains warning-free.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The options parser and CLI helpers are clustered in the tail of `TunPNetBenchmarkRunner.cs`. | verified | source | `src/PNet.Mesh.Benchmarks/TunPNetBenchmarkRunner.cs:1378-1589`. |
| 2 | F | `TunPNetBenchmarkRunnerTests` exercises `TunPNetBenchmarkOptions.TryParse`. | verified | source | `src/PNet.Mesh.Tun.UnitTests/TunPNetBenchmarkRunnerTests.cs:70-112` and related option tests. |

## Completion Report

Resolved in `4cb0090c463832e9b435d141830514303a641ced`.

## Resolving Commits

- `4cb0090c463832e9b435d141830514303a641ced`
