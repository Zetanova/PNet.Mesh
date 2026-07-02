---
issue: 077
date: 2026-07-02
source: performance/refactor
priority: high
status: completed
research-date: 2026-07-02
research-status: complete
assumptions-date: 2026-07-02
split-status: child
parent-issue: 073
completed-date: 2026-07-02
completion-commit: e480ba9
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

## Completion Report

Commit `e480ba9` replaces the six session-specific locks with `_sessionOwnerLock`, funnels packet staging, ACK state, retransmit buffering, endpoint discovery, timers, disposal, and pending control commands through that owner gate, and keeps `StatusChanged` callbacks off the owner lock.

Verification:

| Check | Result |
|---|---|
| `dotnet build PNet.Mesh.sln -c Release --no-restore` | passed; 217 nullable warnings reported |
| `dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` | passed, 194/194 |
| `dotnet format whitespace PNet.Mesh.sln --include <scoped source/test files> --verify-no-changes` | passed |
| Session micro benchmark | 64: 688.6 us, 128: 680.4 us, 512: 697.3 us, 1280: 667.9 us, 1420: 705.9 us |
| Macro benchmark | in-memory 164,531 pkt/s p50 7.9 us; UDP loopback 105,379 pkt/s p50 15.901 us |

Baseline comparison:

| Metric | Baseline | Final |
|---|---:|---:|
| Session micro 128B | 705.8 us, 27.27 KB | 680.4 us, 26.95 KB |
| Session micro 1280B | 781.9 us, 31.28 KB | 667.9 us, 30.96 KB |
| Macro in-memory throughput | 159,581 pkt/s | 164,531 pkt/s |
| Macro UDP loopback throughput | 99,280 pkt/s | 105,379 pkt/s |

Assumptions:

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 4 | F | `e480ba9` removes the issue-targeted session locks and leaves one owner gate for mutable session state. | verified | source | `PNetMeshSession.cs` no longer defines `_openPacketLock`, `_retransBufferLock`, `_receiveStateLock`, `_remoteAckLock`, `_endpointDiscoveryLock`, or `_controlQueueLock`; `_sessionOwnerLock` guards their former state. |
| 5 | F | Covered session behavior still passes after the owner-gate refactor. | verified | test | Full unit run passed 194/194 after adding pending-dispose, ACK-timer, and non-inline-status regression coverage. |
