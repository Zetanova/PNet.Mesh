---
issue: 057
date: 2026-07-02
source: benchmark/phase-4
priority: medium
status: ready
gate: "Wait for #055 core microbenchmarks and #056 macro harnesses."
gate-depends:
  - 55
  - 56
gate-reason: "Requires benchmark suites and macro harnesses before baselines and thresholds can be meaningful."
gate-last-checked: 2026-07-02
gate-status: cleared
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
brief: "description+scope+acceptance-criteria+assumptions+playbook+gate"
views:
  enrich: "description+scope+acceptance-criteria+assumptions+playbook+gate"
  fix: "description+scope+acceptance-criteria+assumptions+playbook+gate"
  complete: "description+completion-report"
---

# 057 - Benchmark Baseline And Regression Policy

## Description

Capture the first benchmark baseline and define how future changes compare performance and allocations. Start with report-only thresholds, then promote stable hot-path budgets to blocking checks when variance is understood.

## Playbook

- `Evidence first`: archive baseline outputs before creating any optimization issue.
- `Separate views`: store microbenchmark and macro-harness summaries separately.
- `Environment`: record OS, CPU, .NET SDK/runtime, git commit, power mode, and run date.
- `Thresholds`: keep initial budgets report-only until repeated runs show stable variance bounds.

## Scope

- Run the BenchmarkDotNet suite on a named machine/runtime and archive summarized results.
- Run macro harnesses and archive summarized results separately from microbenchmarks.
- Document baseline environment: OS, CPU, .NET SDK/runtime, git commit, power mode, and run date.
- Define initial report-only budgets for `ns/op`, `B/op`, `Gen0/1/2`, packets/sec, and latency percentiles.
- Document before/after benchmark workflow for optimization PRs.

## Out of Scope

- Blocking CI on unstable thresholds before variance is known.
- Treating one-off container perf smoke as a hard baseline.

## Acceptance Criteria

- Baseline artifacts or summaries exist for microbenchmarks and macro harnesses.
- Allocation budgets are documented for transport and session hot paths.
- Regression workflow says how to compare against baseline and when to file optimization issues.
- Thresholds start report-only unless repeated runs prove stable variance bounds.

## Gate

Cleared on 2026-07-02: #055 added core protocol microbenchmarks in `15537b4`, and #056 added macro harnesses in `64aa683`.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Benchmarks need environment metadata to be comparable. | verified | source | BenchmarkDotNet reports runtime and environment metadata; macro harnesses should emit equivalent fields. |
| 2 | R | Allocation budgets should start report-only until variance and workload realism are understood. | verified | logical | Premature blocking thresholds can reject harmless noise before baseline stability is known. |
| 3 | F | Transport and session hot paths need documented max `B/op` after baseline capture. | verified | source | The rollout explicitly asks for memory allocations to be measured, tested, and benchmarked. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-02 | `gate-depends: [055, 056]` | source | ready | #055 and #056 are completed, so #057 is implementation-ready. |

## Validation History

- 2026-07-02: dependency gates cleared by #055 and #056; #057 is now ready.
