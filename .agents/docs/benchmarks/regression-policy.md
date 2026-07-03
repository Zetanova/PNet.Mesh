---
title: Benchmark Regression Policy
assumptions-date: 2026-07-02
status: report-only
baseline: 2026-07-02-baseline.md
---

# Benchmark Regression Policy

## Current Mode

Benchmark checks are report-only. They inform review and optimization issue creation, but they do not block commits until repeated runs establish stable variance bounds.

Any source change claimed to improve performance must include before/after micro and macro benchmark evidence. Regressions in latency, throughput, allocation, or GC behavior need an explicit, source-backed justification in the change report.

## Baseline

Current baseline: [2026-07-02-baseline.md](2026-07-02-baseline.md).

Use a new baseline only when benchmark code, runtime, host hardware, or macro workload shape intentionally changes. Keep old baselines as historical references.

Privileged TUN comparison runs use [tun-workflow.md](tun-workflow.md) because they require runner capabilities beyond normal micro and macro benchmarks.

## Before/After Workflow

1. Build Release:

   ```bash
   dotnet restore PNet.Mesh.sln
   dotnet build PNet.Mesh.sln -c Release --no-restore
   ```

2. Run full microbenchmarks:

   ```bash
   timeout 900s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*'
   ```

3. Run macro harnesses:

   ```bash
   timeout 180s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --macro all --payload 128 --warmup 00:00:05 --duration 00:00:30
   ```

4. Compare against the current baseline and include the comparison in the change report.

5. For performance improvement changes, record whether the micro and macro results show improvement, no material change, or regression.

6. File an optimization issue when the same hot path exceeds report thresholds on two comparable runs, or when a single run shows an obvious large regression with no workload explanation.

## Report Thresholds

Microbenchmark report thresholds:

- Mean time: report when the same method/payload is more than 20% slower than baseline.
- Throughput: report when `Op/s` is more than 20% lower than baseline.
- Allocation: report when `Allocated` is more than 20% higher than baseline, or when a path that was `0 B/op` starts allocating.
- GC: report when Gen0/1/2 counts increase by more than 20%, or when a previously zero Gen1/Gen2 path starts collecting.

Macro report thresholds:

- Packets/sec, payload bytes/sec, or wire bytes/sec: report when more than 20% lower than baseline.
- p95 or p99 latency: report when more than 20% higher than baseline.
- Allocated bytes per packet: report when more than 20% higher than baseline.
- Gen1/Gen2 collections: report any new sustained increase.

TUN report thresholds stay manual review items until the TUN workflow records repeated successful runs for one privileged runner class.

## Promotion To Blocking

Do not make performance thresholds blocking until all are true:

- At least three baselines exist for the same host class and runtime.
- The selected threshold has low enough variance that normal host noise does not exceed it.
- The check has a fast failure mode and prints the compared baseline, current value, and percent delta.
- The threshold protects a hot path already proven by issue or production evidence.

## Optimization Issue Trigger

When filing a performance issue, include:

- Baseline file and run date.
- Current run command and environment.
- Method/scenario, payload size, metric, baseline value, current value, and percent delta.
- Whether the regression is CPU time, throughput, allocation, GC, or latency.
- Any intentional workload/runtime changes that explain the result.
- Source-backed justification for any accepted regression.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The current baseline is a single complete run on one host/runtime. | verified | source | `2026-07-02-baseline.md` records one full microbenchmark run and one macro run. |
| 2 | R | A 20% report threshold is appropriate before variance is known. | verified | logical | It is wide enough to avoid most noise while still surfacing meaningful shifts for review. |
| 3 | R | Blocking checks need at least three comparable baselines. | verified | logical | One run cannot establish stable host variance for benchmark gating. |
