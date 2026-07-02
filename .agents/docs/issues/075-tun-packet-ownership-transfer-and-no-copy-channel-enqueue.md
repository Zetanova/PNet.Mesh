---
issue: 075
date: 2026-07-02
source: performance/analysis
priority: medium
status: open
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

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The TUN reader currently copies packets into a new array. | verified | source | `PNetMeshTunBridge.RunTunReaderAsync` calls `ToArray()` on the packet payload. |
| 2 | F | The channel enqueue path currently rents pooled memory and copies the payload. | verified | source | `PNetMeshChannel.EnqueueWriteAsync` rents a buffer and copies packet bytes into it. |
| 3 | F | The send path already crosses an async channel boundary. | verified | source | The packet hand-off flows from the TUN bridge into `PNetMeshChannel` enqueue methods. |
