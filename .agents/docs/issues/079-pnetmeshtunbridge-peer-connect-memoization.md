---
issue: 079
date: 2026-07-02
source: performance/refactor
priority: high
status: open
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

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `PNetMeshTunBridge.PeerState` currently uses `SemaphoreSlim` to serialize first connect and cache population. | verified | source | `PNetMeshTunBridge.cs` defines `_connectLock` and uses it in `GetChannelAsync`. |
| 2 | F | The current connect path is isolated from the session locks. | verified | source | The TUN bridge connect serialization lives in `PNetMeshTunBridge.PeerState`, not `PNetMeshSession`. |
| 3 | F | The current TUN bridge tests focus on packet exchange and burst forwarding. | verified | source | `PNetMeshTunBridgeTests` cover forwarding behavior, not concurrent connect memoization. |
