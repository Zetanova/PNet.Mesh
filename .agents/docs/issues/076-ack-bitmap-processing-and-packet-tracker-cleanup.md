---
issue: 076
date: 2026-07-02
source: performance/analysis
priority: medium
status: completed
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - 7482232
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

# 076 - ACK Bitmap Processing And Packet Tracker Cleanup

## Description

Replace ACK bitmap copying with span-based bit tests and verify whether packet-tracker cleanup can shrink its rented bitmap window safely.

## Parent Tracking

- Parent: #072
- Extracted scope: ACK bitmap processing and packet-tracker cleanup.
- Standalone reason: ACK parsing and packet-tracker sizing can be validated with dedicated session regressions and benchmark proof, separate from TUN and helper work.

## Research

The session ACK path still materializes bytes for bitmap inspection, and the packet buffer still uses `BitArray` plus LINQ. The packet tracker also carries a `todo smaller implementation` note, so its ring sizing needs focused verification rather than an umbrella optimization pass.

## Scope

- In scope: `ToByteArray()` removal in ACK processing, `BitArray`/LINQ replacement, packet-tracker window math review, retained duplicate-copy behavior if benchmark evidence is insufficient.
- Out of scope: TUN packet ownership, span/IP helper work, wire-format changes.

## Acceptance Criteria

- ACK processing no longer materializes arrays solely for bitmap inspection.
- Packet-tracker sizing either stays unchanged with documented rationale or shrinks with passing tests.
- Session regression coverage proves ACK and retransmit behavior remains correct.
- Benchmarks capture any allocation difference in the ACK/session paths.

## Completion Report

Implemented in `7482232`.

- Replaced ACK bitmap materialization in `PNetMeshSession.ProcessAck` with `ByteString.Memory` and a `ReadOnlyMemory<byte>` ACK snapshot.
- Replaced packet-buffer `BitArray`/LINQ bitmap traversal with direct bit tests over `ReadOnlyMemory<byte>`, while keeping the original `byte[]` overloads as compatibility forwarders.
- Shrunk `PNetMeshPacketTracker` rented storage to the word count required by the effective replay window while preserving the prior public `Size` behavior, `ceil(counterSize / 8) * 8`.
- Fixed exact-byte bitmap writes and the exact full-byte `RightShift` trim path so fully cumulative ACK bitmaps produce an empty out-of-sequence bitmap without throwing.
- Added regression coverage for tracker sizing, exact-byte bitmap writes, exact full-byte `RightShift`, and updated routing ACK assertions.

Verification on 2026-07-02:

- Confirmed the new `bitmap_right_shift_handles_exact_full_byte_regression` failed before the `RightShift` fix with `IndexOutOfRangeException`.
- `timeout 180s rtk dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh/PNetMeshSession.cs src/PNet.Mesh/PNetMeshPacketBuffer.cs src/PNet.Mesh/PNetMeshPacketTracker.cs src/PNet.Mesh.UnitTests/PNetMeshRoutingUnitTests.cs src/PNet.Mesh.UnitTests/PNetMeshPacketBufferTests.cs src/PNet.Mesh.UnitTests/PNetMeshPacketTrackerTest.cs --no-restore --verify-no-changes --verbosity minimal` passed.
- `timeout 240s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` passed with 0 errors and 217 existing nullable warnings from the dirty tree.
- Focused `PNetMeshPacketTrackerTest` passed after edits, 27/27 tests.
- Focused `PNetMeshPacketBufferTests`, `PNetMeshPacketTrackerTest`, and `PNetMeshRoutingUnitTests` passed after edits, 96/96 tests.
- `timeout 600s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*SessionWriteReadPayloadPacket*'` completed as broad session benchmark evidence; no dedicated packet-buffer/tracker microbenchmark was added.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Before #076 implementation, `PNetMeshSession.ProcessAck` called `ToByteArray()`. | verified | source | The ACK path materialized a byte array before bitmap handling. |
| 2 | F | Before #076 implementation, `PNetMeshPacketBuffer` used `BitArray` and LINQ for bitmap processing. | verified | source | The packet buffer helper allocated a `BitArray` and used LINQ enumeration. |
| 3 | F | Before #076 implementation, `PNetMeshPacketTracker` had a `todo smaller implementation` note and required semantic verification before changing. | verified | source | `PNetMeshPacketTracker.cs` included the `todo smaller implementation` comment. |
