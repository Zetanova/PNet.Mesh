---
issue: 058
date: 2026-07-02
source: benchmark/phase-5
priority: medium
status: completed
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
terminal-state: completed
completed-date: 2026-07-02
completed: 2026-07-02
completed-commits:
  - 720e48a25d06d53af13d5a3f66345d67daf443f9
brief: "description+completion-report"
views:
  enrich: "description+scope+acceptance-criteria+assumptions+playbook+gate"
  fix: "description+scope+acceptance-criteria+assumptions+playbook+gate"
  complete: "description+completion-report"
---

# 058 - Post-Baseline Benchmark Optimization Backlog

## Description

After baseline capture, use benchmark evidence to file focused optimization issues by measured hotspot. This parent issue prevents speculative performance work before benchmark results exist.

## Playbook

- `Evidence gate`: create child issues only from measured hotspots, not guesses.
- `One hotspot`: keep each child issue focused on one benchmark result and one target metric.
- `Allocation focus`: create allocation-focused children whenever B/op or GC pressure is material.
- `Traceability`: each child issue needs benchmark evidence, target metric, and verification command.

## Scope

- Review BenchmarkDotNet and macro baseline output for top CPU and allocation hotspots.
- Create child issues only for measured problems with benchmark evidence.
- Candidate topics: BLAKE2s hash/MAC span-to-array allocations, session protobuf `ToArray` parse path, PNet frame allocation, transport padding/rent overhead, replay tracker bitmap cost, channel/session lock or channel overhead, UDP loopback send/receive bottlenecks.
- Each child issue must include benchmark evidence, target metric, and verification command.
- Update tracker dependencies so optimization issues are gated on the baseline they improve.

## Out of Scope

- Implementing optimizations directly in the parent issue.
- Filing optimization issues without measured evidence.

## Acceptance Criteria

- A benchmark evidence review identifies zero or more optimization child issues.
- Every child issue names the benchmark, baseline metric, target metric, and expected verification command.
- If no material hotspots exist, this issue can close with a no-child rationale and retained baseline evidence.
- Allocation-focused child issues are created for any hot path with material `B/op` or GC pressure.

## Gate

Cleared on 2026-07-02: #057 documented the benchmark baseline and regression policy in `8af78e0`.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | R | Optimization work should be driven by measured benchmark hotspots rather than guesses. | verified | logical | The benchmark rollout exists to produce evidence before optimization. |
| 2 | F | Potential allocation hotspots include hash/MAC byte-array conversions, protobuf `ToArray` parse, PNet frame allocation, and padding buffers. | verified | source | These paths were identified from current source-level benchmark planning. |
| 3 | F | Post-baseline child issues must include allocation targets when allocation pressure is the measured problem. | verified | source | The rollout explicitly asks for memory allocations to be measured, tested, and benchmarked. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-02 | `gate-depends: [057]` | source | ready | #057 is completed, so #058 is implementation-ready. |

## Validation History

- 2026-07-02: dependency gate cleared by #057; #058 is now ready.

## Completion Report

Implemented in `720e48a`.

Created measured optimization child issues from the #057 baseline:

- #066: reduce WireGuard hash/MAC allocation pressure measured in handshake and rejection benchmarks.
- #067: reduce session protobuf/frame allocation pressure measured in session and in-memory macro benchmarks.
- #068: add a non-allocating or pooled PNet frame creation path for payload framing hotspots.
- #069: reduce UDP loopback macro harness receive allocation to isolate protocol cost.

No speculative optimization work was implemented in this parent issue. Each child includes baseline metric, target area, and verification command.

Verification:
- `md .agents/docs/discovered-issues.md .agents/docs/issues/066-wireguard-hash-mac-allocation-hotspot.md .agents/docs/issues/067-session-protobuf-frame-allocation-hotspot.md .agents/docs/issues/068-pnet-frame-creation-allocation-hotspot.md .agents/docs/issues/069-udp-loopback-macro-allocation-hotspot.md` passed.
- `git diff --check` passed.

## Resolving Commits

- `720e48a25d06d53af13d5a3f66345d67daf443f9` - issues: add benchmark optimization backlog
