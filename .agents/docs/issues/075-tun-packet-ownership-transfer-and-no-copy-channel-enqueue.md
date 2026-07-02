---
issue: 075
date: 2026-07-02
source: performance/analysis
priority: medium
status: completed
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - 4619f47
research-date: 2026-07-02
research-status: complete
assumptions-date: 2026-07-02
split-status: child
parent-issue: 072
brief: "description+parent-tracking+research+scope+acceptance-criteria+assumptions"
views:
  enrich: "description+parent-tracking+research+scope+acceptance-criteria+assumptions"
  fix: "description+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 075 - TUN Packet Ownership Transfer And No-Copy Channel Enqueue

## Description

Transfer TUN packets through ownership instead of copying them twice so `PNetMeshChannel` can enqueue owned buffers without redundant allocations.

## Parent Tracking

- Parent: #072
- Extracted scope: TUN read packet ownership transfer and no-copy channel enqueue.
- Standalone reason: this change only affects packet hand-off and queueing lifetime, not the helper or ACK cleanup slices.

## Research

`PNetMeshTunBridge.RunTunReaderAsync` still materializes a new array from the TUN packet, and `PNetMeshChannel.EnqueueWriteAsync` still rents channel memory and copies the payload. That gives this slice a focused ownership problem with a clear queue boundary.

## Scope

- In scope: TUN reader ownership transfer, no-copy async enqueue overload in `PNetMeshChannel`, sensitive span clearing in pooled-buffer paths, queue lifetime validation.
- Out of scope: ACK bitmap cleanup, packet-tracker sizing, span/IP helper work, protobuf wire-format changes.

## Acceptance Criteria

- TUN packets can be enqueued without an extra copy when ownership is transferred.
- Queued buffers remain valid for downstream readers.
- Tests prove no double-disposal or use-after-free behavior, and benchmarks show reduced allocation or copy pressure.

## Completion Report

Implemented in `4619f47`.

- Added owner-taking `PNetMeshChannel.EnqueueWriteAsync` and `EnqueueUnreliableWriteAsync` overloads that transfer an `IMemoryOwner<byte>` without copying payload bytes.
- Preserved the existing copying enqueue overloads and documented the ownership contract on the new public overloads.
- Updated `PNetMeshTunBridge` to rent one packet owner per TUN read, queue payload plus owner, and transfer that owner into the channel.
- Added cleanup for queued channel commands and TUN peer queues so cancellation/dispose paths clear and dispose any unconsumed owners.
- Added routing and TUN bridge regressions for copied enqueue preservation, owned enqueue transfer, send/dispose cleanup, queued owner drain, and late enqueue rejection after drain.
- Added `ChannelEnqueueBenchmarks` comparing copied and owned channel enqueue paths.

Verification on 2026-07-02:

- Baseline focused TUN bridge tests passed before edits, 5/5 tests.
- Baseline focused routing tests passed before edits, 66/66 tests.
- `timeout 180s rtk dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh/PNetMeshChannel.cs src/PNet.Mesh.Tun/PNetMeshTunBridge.cs src/PNet.Mesh.UnitTests/PNetMeshRoutingUnitTests.cs src/PNet.Mesh.Tun.UnitTests/PNetMeshTunBridgeTests.cs src/PNet.Mesh.Benchmarks/ChannelEnqueueBenchmarks.cs --no-restore --verify-no-changes --verbosity minimal` passed.
- `timeout 240s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` passed with 0 errors and 0 warnings.
- Focused `PNetMeshRoutingUnitTests` passed after edits, 69/69 tests.
- Focused `PNetMeshTunBridgeTests` passed after edits, 7/7 tests.
- `timeout 600s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*ChannelEnqueue*'` completed; BenchmarkDotNet could not set high process priority in this environment.

Benchmark evidence from `ChannelEnqueueBenchmarks`:

| Method | PayloadSize | Mean | Op/s | Allocated |
|---|---:|---:|---:|---:|
| Channel enqueue copied payload | 128 | 252.9 ns | 3,954,025.7 | 88 B |
| Channel enqueue owned payload | 128 | 285.8 ns | 3,499,349.9 | 88 B |
| Channel enqueue copied payload | 1280 | 434.5 ns | 2,301,566.0 | 88 B |
| Channel enqueue owned payload | 1280 | 289.4 ns | 3,455,596.3 | 88 B |

The benchmark isolates channel enqueue rather than full privileged TUN throughput. It shows the owned path removes the enqueue-time payload copy; managed allocations remain unchanged because both benchmark paths still allocate the async/channel command shape.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Before #075 implementation, the TUN reader copied packets into a new array. | verified | source | `PNetMeshTunBridge.RunTunReaderAsync` called `ToArray()` on the packet payload. |
| 2 | F | Before #075 implementation, the channel enqueue path rented pooled memory and copied the payload. | verified | source | `PNetMeshChannel.EnqueueWriteAsync` rented a buffer and copied packet bytes into it. |
| 3 | F | The send path already crosses an async channel boundary. | verified | source | The packet hand-off flows from the TUN bridge into `PNetMeshChannel` enqueue methods. |
