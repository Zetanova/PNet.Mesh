---
issue: 079
date: 2026-07-02
source: performance/refactor
priority: high
status: completed
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - c89ada0
research-date: 2026-07-02
research-status: complete
assumptions-date: 2026-07-02
split-status: child
parent-issue: 073
brief: "description+parent-tracking+research+scope+acceptance-criteria+assumptions"
views:
  enrich: "description+parent-tracking+research+scope+acceptance-criteria+assumptions"
  fix: "description+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 079 - PNetMeshTunBridge Peer Connect Memoization

## Description

Replace `PNetMeshTunBridge.PeerState` connect serialization with lock-free async memoization if it still returns one cached channel.

## Parent Tracking

- Parent: #073
- Extracted scope: peer connect memoization.
- Standalone reason: TUN bridge connect serialization is independent from session ownership and channel relay-state signaling.

## Research

`PNetMeshTunBridge.PeerState` still uses `SemaphoreSlim` to serialize first connect and cache population. The current tests cover packet exchange and burst forwarding, so this slice needs targeted concurrency proof rather than broader protocol work.

## Scope

- In scope: `PeerState._connectLock` removal, one cached channel per peer connect path, concurrency test for simultaneous startup, TUN-path validation.
- Out of scope: session mailbox refactor, relay-state atomics, packet ownership transfer, wire protocol changes.

## Acceptance Criteria

- Concurrent callers still observe exactly one cached channel for a peer connect sequence.
- The connect path remains simple and does not create duplicate startup work.
- Targeted concurrency tests cover simultaneous `GetChannelAsync` callers.
- Report notes explain any TUN-host gating if the host cannot run privileged comparisons.

## Completion Report

Implemented in `c89ada0`.

- Replaced `PeerState._connectLock` with a memoized single-flight `Task<PNetMeshChannel>`.
- Cached the connected channel after the shared connect completes.
- Reset the memoized task on shared connect failure so later callers can retry.
- Decoupled per-caller cancellation from the shared connect task, so one canceled waiter cannot cancel the memoized connect for other callers.
- Added targeted tests for simultaneous `GetChannelAsync` callers sharing one connect task/channel and for canceled waiters not poisoning the shared connect.

Verification on 2026-07-02:

- Baseline focused `PNetMeshTunBridgeTests` passed before edits, 3/3 tests.
- `timeout 180s rtk dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh.Tun/PNetMeshTunBridge.cs src/PNet.Mesh.Tun.UnitTests/PNetMeshTunBridgeTests.cs --no-restore --verify-no-changes --verbosity minimal` passed.
- `timeout 240s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` passed with 0 errors and 0 warnings after the parent correction.
- Focused `PNetMeshTunBridgeTests` passed after the parent correction, 5/5 tests.
- Privileged host TUN comparison was not required for this isolated fake-device peer-state slice; the existing fake-device TUN path tests passed.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Before #079 implementation, `PNetMeshTunBridge.PeerState` used `SemaphoreSlim` to serialize first connect and cache population. | verified | source | `PNetMeshTunBridge.cs` defined `_connectLock` and used it in `GetChannelAsync`. |
| 2 | F | The pre-implementation connect path was isolated from the session locks. | verified | source | The TUN bridge connect serialization lived in `PNetMeshTunBridge.PeerState`, not `PNetMeshSession`. |
| 3 | F | Before #079 implementation, the TUN bridge tests focused on packet exchange and burst forwarding. | verified | source | `PNetMeshTunBridgeTests` covered forwarding behavior, not concurrent connect memoization. |
