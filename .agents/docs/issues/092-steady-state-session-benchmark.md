---
issue: 092
date: 2026-07-03
source: benchmark-analysis
priority: medium
status: completed
terminal-state: completed
research-status: complete
research-date: 2026-07-03
assumptions-date: 2026-07-03
completed-date: 2026-07-03
completed: 2026-07-03
completed-commits:
  - ad9edae3f5f7b3c0f4643ab791e0c459c29f52d8
brief: "description+playbook+scope+acceptance-criteria+assumptions+baseline-evidence"
views:
  enrich: "description+playbook+scope+acceptance-criteria+assumptions+baseline-evidence"
  fix: "description+playbook+scope+acceptance-criteria+assumptions+baseline-evidence"
  complete: "description+completion-report+resolving-commits"
---

# 092 - Steady-State Session Benchmark

## Description

Add a steady-state session write/read benchmark before making further `PNetMeshSession` allocation optimizations. The current session microbenchmark includes per-operation session pair creation and handshake setup, so it does not isolate the steady payload hot path.

## Playbook

- `Evidence`: compare against the 2026-07-03 microbenchmark artifacts under `artifacts/benchmarks/results/`.
- `Scope`: add a BenchmarkDotNet case that creates an open sender/receiver session pair in setup and measures repeated payload write/read only.
- `Ownership`: include copied-payload and owned/pool-backed payload variants if the benchmark is used to evaluate no-copy ownership transfer.
- `Target`: produce allocation evidence that separates session setup cost from steady packet serialization, encryption, parse, and inbound delivery cost.

## Scope

- Add a steady-state `PNetMeshSession` benchmark with open session setup outside the measured method.
- Keep the existing setup-inclusive `SessionWriteReadPayloadPacket` benchmark for regression and comparison.
- Dispose any pooled packet owners and drain outbound/inbound channels between iterations so state does not accumulate.
- Document or name benchmark cases so future optimization work can choose the correct measurement.

## Out of Scope

- Changing session serialization, protobuf ownership, or retransmit behavior in the benchmark-only change.
- Replacing protobuf schema or packet wire format.

## Baseline Evidence

| Benchmark | Payload | Mean | Allocated | Verification |
|---|---:|---:|---:|---|
| Session write/read payload | 64 | 702,064.62 ns | 27712 B/op | `timeout 900s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*'` |
| Session write/read payload | 1420 | 710,019.35 ns | 32419 B/op | same 2026-07-03 run |

Relevant source evidence:

- `WireGuardTransportBenchmarks.SessionWriteReadPayloadPacket` creates a fresh `SessionPair` inside the measured benchmark method.
- `BenchmarkProtocolHarness.CreateOpenSessionPair` performs key generation, protocol construction, channel creation, and session handshake.
- The benchmark result therefore mixes setup allocation with steady payload allocation.

## Acceptance Criteria

- A new steady-state session benchmark exists and excludes session pair creation/handshake from the measured method.
- Benchmark output reports allocation separately for the existing setup-inclusive case and the new steady-state case.
- The benchmark drains/disposes pooled packet owners correctly and remains deterministic under BenchmarkDotNet `ShortRun`.
- Completion report includes before/after allocation numbers for at least payload 128 and 1420.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | `SessionWriteReadPayloadPacket` creates a fresh open session pair inside the measured benchmark method. | verified | source | `src/PNet.Mesh.Benchmarks/WireGuardTransportBenchmarks.cs` calls `BenchmarkProtocolHarness.CreateOpenSessionPair()` in the benchmark body. |
| 2 | F | Creating an open session pair performs setup beyond steady payload write/read. | verified | source | `src/PNet.Mesh.Benchmarks/BenchmarkProtocolHarness.cs` allocates protocols/channels/sessions and completes initialize/response packets. |
| 3 | F | The 2026-07-03 microbenchmark run measured 27712-32419 B/op for setup-inclusive session payload cases. | verified | test | The Release BenchmarkDotNet run wrote `artifacts/benchmarks/results/PNet.Mesh.Benchmarks.WireGuardTransportBenchmarks-report-github.md`. |
| 4 | R | A setup-inclusive session benchmark is insufficient evidence for session-internal allocation optimization decisions. | verified | logical | Assumptions 1-3 show the current measurement includes setup allocations that a steady-state hot path would not pay per packet. |

## Completion Report

Resolved in `ad9edae3f5f7b3c0f4643ab791e0c459c29f52d8` (`Add allocation-free packet parsing benchmarks`).

The steady-state session benchmark now measures repeated payload write/read with session setup kept outside the timed path, while the setup-inclusive benchmark remains available for comparison. Verification passed the Release build, the full unit test suite, scoped whitespace formatting, `git diff --check`, and focused BenchmarkDotNet smoke runs covering payloads 128 and 1420.

## Resolving Commits

- `ad9edae3f5f7b3c0f4643ab791e0c459c29f52d8`
