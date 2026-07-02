---
issue: 055
date: 2026-07-02
source: benchmark/phase-2
priority: medium
status: completed
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
terminal-state: completed
completed-date: 2026-07-02
completed: 2026-07-02
completed-commits:
  - 15537b4b022fd525a5f320ea142ae8653dd0013b
brief: "description+completion-report"
views:
  enrich: "description+scope+acceptance-criteria+assumptions+playbook+gate"
  fix: "description+scope+acceptance-criteria+assumptions+playbook+gate"
  complete: "description+completion-report"
---

# 055 - Core Protocol Microbenchmarks

## Description

Add the first set of protocol microbenchmarks after the BenchmarkDotNet project exists. Measure CPU time and allocations for the WireGuard-only protocol paths without sockets, Docker, or test harness overhead.

## Playbook

- `Setup`: pre-generate keys and PSKs in `GlobalSetup` unless the benchmark explicitly measures setup cost.
- `Categories`: label benchmarks so handshake, transport, framing, and session paths can be filtered independently.
- `Isolation`: keep microbenchmarks network-free and avoid Testcontainers or UDP sockets.
- `Metrics`: record time plus allocation metrics for every payload size and every hot path benchmark.

## Scope

- Benchmark Noise/WireGuard initiation, response, and full handshake setup.
- Benchmark `PNetMeshTransport2.WriteMessage` and `TryReadMessage` for payload sizes 64, 128, 512, 1280, and 1420.
- Benchmark tamper rejection, replay rejection, and unknown receiver-index rejection where setup cost can be isolated.
- Benchmark `PNetMeshPayloadFraming.CreatePNet`/`TryRead` and IPv4/IPv6 detection paths.
- Benchmark `PNetMeshSession` protobuf serialize/frame/encrypt and decrypt/frame/parse paths.
- Record `B/op` and `Gen0/1/2` for every benchmark.

## Out of Scope

- Network throughput tests.
- Optimizations based only on intuition rather than benchmark output.

## Acceptance Criteria

- Microbenchmark suite can be filtered by category, e.g. handshake, transport, framing, session.
- Benchmark output includes time and allocation metrics for each configured payload size.
- Benchmarks pre-generate keys and PSKs in `GlobalSetup` except for tests explicitly measuring setup cost.
- No Testcontainers or UDP sockets are used in BenchmarkDotNet microbenchmarks.

## Gate

Cleared on 2026-07-02: #054 created the benchmark project and standard allocation reporting in `a402a8a`.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The WireGuard-only implementation exposes protocol hot paths in `PNetMeshProtocol`, `PNetMeshTransport2`, `PNetMeshPayloadFraming`, and `PNetMeshSession`. | verified | source | Current source contains those classes and tests exercise the relevant paths. |
| 2 | R | Network-free microbenchmarks give more stable protocol cost data than UDP/Testcontainers runs. | verified | logical | This follows from isolating protocol code from OS scheduling, Docker startup, and socket buffers. |
| 3 | F | Allocation measurements are required for benchmark acceptance. | verified | source | The rollout asks for memory allocations to be measured, tested, and benchmarked. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-02 | `gate-depends: [054]` | source | ready | #054 is completed, so #055 is implementation-ready. |

## Validation History

- 2026-07-02: dependency gate cleared by #054; #055 is now ready.

## Completion Report

Implemented in `15537b4`.

- Added category-filterable microbenchmarks for transport, framing, handshake, rejection, and session paths.
- Added in-memory `PNetMeshSession` setup for session serialization/frame/encrypt and decrypt/frame/parse coverage.
- Exposed internals to the benchmark assembly so the benchmark harness can avoid sockets and Testcontainers.

Verification:
- `dotnet build PNet.Mesh.sln -c Release --no-restore` passed.
- `dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --list flat` listed the seven benchmark methods.
- `dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --filter '*SessionWriteReadPayloadPacket*'` passed and produced `Gen1`, `Gen2`, `Op/s`, `Gen0`, and `Allocated` columns.
- `dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --filter '*RejectInvalidTransportPackets*'` passed and produced the same allocation columns.
- `dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` passed 154 tests.

## Resolving Commits

- `15537b4b022fd525a5f320ea142ae8653dd0013b` - benchmarks: add core protocol microbenchmarks
