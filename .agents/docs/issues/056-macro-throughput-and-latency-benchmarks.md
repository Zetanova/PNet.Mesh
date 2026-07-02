---
issue: 056
date: 2026-07-02
source: benchmark/phase-3
priority: medium
status: ready
gate: "Wait for #055 core protocol microbenchmarks."
gate-depends:
  - 55
gate-reason: "Requires core microbenchmarks so macro results can be interpreted against protocol hot-path costs."
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

# 056 - Macro Throughput And Latency Benchmarks

## Description

Add Release-only macro benchmark harnesses for end-to-end throughput and latency. These are not BenchmarkDotNet microbenchmarks; they should report realistic session and UDP behavior with explicit warmup, duration, packet counts, bytes, latency percentiles, and allocation counters.

## Playbook

- `In-memory`: measure a two-session payload exchange without Docker or sockets.
- `UDP loopback`: measure encrypted packet exchange over localhost UDP.
- `Smoke only`: keep optional TestNode or container perf smoke manual and clearly non-deterministic.
- `Timing`: fix warmup and measurement durations so runs are comparable across hosts.

## Scope

- Add an in-memory two-session payload exchange harness.
- Add a localhost UDP loopback harness for encrypted packet exchange.
- Add optional manual TestNode/container perf smoke marked non-deterministic.
- Report packets/sec, bytes/sec, p50/p95/p99 latency, total allocated bytes, GC collection counts, runtime, OS, CPU, and payload size.
- Support fixed warmup and measurement durations, e.g. 5-10s warmup and 30-60s measurement.

## Out of Scope

- Making Docker/Testcontainers results blocking in CI.
- Replacing BenchmarkDotNet microbenchmarks.

## Acceptance Criteria

- In-memory harness runs in Release without Docker and emits machine-readable summary output.
- UDP loopback harness runs in Release and reports throughput and latency percentiles.
- Allocation data is captured with `GC.GetAllocatedBytesForCurrentThread` or documented process-level counters.
- Container perf smoke is optional/manual and documented as non-deterministic.

## Gate

Cleared on 2026-07-02: #055 added category-filterable core protocol microbenchmarks in `15537b4`.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Existing E2E coverage uses Testcontainers for mesh topology validation. | verified | source | `src/PNet.Mesh.E2ETests` contains Testcontainers-backed harness tests. |
| 2 | R | UDP and Docker timing are less stable than in-process microbenchmarks. | verified | logical | They include OS scheduling, socket buffering, container startup, and host load effects. |
| 3 | F | Macro harnesses must include allocation data as part of benchmark output. | verified | source | The rollout asks for memory allocations to be measured, tested, and benchmarked. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-02 | `gate-depends: [055]` | source | ready | #055 is completed, so #056 is implementation-ready. |

## Validation History

- 2026-07-02: dependency gate cleared by #055; #056 is now ready.
