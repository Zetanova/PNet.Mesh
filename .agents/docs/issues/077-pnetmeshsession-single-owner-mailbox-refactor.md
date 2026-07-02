---
issue: 077
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

# 077 - PNetMeshSession Single-Owner Mailbox Refactor

## Description

Move `PNetMeshSession` mutable state behind one serialized command path so the session no longer relies on piecemeal locks for core ownership.

## Parent Tracking

- Parent: #073
- Extracted scope: session mailbox refactor and redundant session lock removal.
- Standalone reason: session serialization can be validated independently of channel relay atomics and TUN connect memoization.

## Research

`PNetMeshSession` still defines separate locks for open packet, retransmit buffer, receive state, remote ACK, endpoint discovery, and control queue state. The session is also touched by server receive handling, channel send and relay processing, timer callbacks, and disposal, which makes this a clean owner-model refactor slice.

## Scope

- In scope: a per-session mailbox or equivalent single-reader actor, payload send and relay send routing, inbound encrypted packet processing, ACK handling, retransmit tick, cumulative ACK tick, endpoint discovery, close, and dispose through the session owner, removal of redundant session locks.
- Out of scope: wire protocol changes, ACK semantics, routing behavior, broad concurrent collections, TUN connect memoization, relay-state lock removal.

## Acceptance Criteria

- `PNetMeshSession` mutations are funneled through one serialized control path.
- Redundant session locks are removed or justified only where a concrete concurrent caller remains.
- Existing regression coverage for ACK, retransmit, out-of-order receive, pending send disposal, endpoint promotion, and timer-driven flush continues to pass.
- Micro and macro benchmark comparisons are captured before and after.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `PNetMeshSession` currently has separate locks for open packet, retransmit buffer, receive state, remote ACK, endpoint discovery, and control queue state. | verified | source | `PNetMeshSession.cs` defines `_openPacketLock`, `_retransBufferLock`, `_receiveStateLock`, `_remoteAckLock`, `_endpointDiscoveryLock`, and `_controlQueueLock`. |
| 2 | F | `PNetMeshSession` is currently shared by server receive handling, channel send and relay processing, timer callbacks, and dispose paths. | verified | source | `PNetMeshServer.ProcessControl`, `PNetMeshChannel.ProcessControl`, relay send paths, and session timers all touch session state. |
| 3 | F | The repository already has benchmark entry points for session micro and macro paths. | verified | source | `WireGuardTransportBenchmarks` and `MacroBenchmarkRunner` already expose session and macro benchmark commands. |
