---
issue: 078
date: 2026-07-02
source: performance/refactor
priority: high
status: completed
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - a8c2a6b
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

# 078 - PNetMeshChannel Relay-State Atomic Signaling

## Description

Replace the channel relay-state lock with atomic `TaskCompletionSource` signaling if waiter and cancellation behavior remains correct.

## Parent Tracking

- Parent: #073
- Extracted scope: relay-state atomic signaling.
- Standalone reason: the relay-state swap is isolated from the session mailbox and TUN connect memoization.

## Research

`PNetMeshChannel` has a small relay-state lock around a `TaskCompletionSource` swap, and the lock is only used by relay wait and cancel signaling helpers. That makes the atomic-swap refactor narrow enough to validate independently.

## Scope

- In scope: `PNetMeshChannel._relayStateLock` removal, `Volatile.Read` and `Interlocked.Exchange` around relay-state signaling, waiter/cancellation/dispose regression tests.
- Out of scope: session mailbox refactor, TUN connect memoization, wire protocol changes, concurrent collection rewrites.

## Acceptance Criteria

- Relay-state waiters still observe the correct completion and cancellation behavior.
- Lock-free signaling does not introduce double-completion or lost wakeups.
- Targeted tests cover pending relay wait, cancellation, and dispose behavior.
- Benchmarks show no meaningful regression on relay-heavy send paths.

## Completion Report

Implemented in `a8c2a6b`.

- Removed `PNetMeshChannel._relayStateLock`.
- Replaced relay-state waiter reads with `Volatile.Read` and relay-state wakeups with `Interlocked.Exchange`.
- Added a regression proving a pending relay wait wakes and fails correctly when the channel is disposed.
- Existing relay wait and cancellation tests continue to cover pending relay wait, later-routable-session wakeup, and cancellation while queued behind a pending relay.
- No relay-specific benchmark exists, so `SessionWriteReadPayloadPacket` was used as the closest available benchmark proxy.

Verification on 2026-07-02:

- Baseline focused `PNetMeshRoutingUnitTests` passed before edits, 65/65 tests.
- `timeout 180s rtk dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh/PNetMeshChannel.cs src/PNet.Mesh.UnitTests/PNetMeshRoutingUnitTests.cs src/PNet.Mesh.Tun/PNetMeshTunBridge.cs src/PNet.Mesh.Tun.UnitTests/PNetMeshTunBridgeTests.cs --no-restore --verify-no-changes --verbosity minimal` passed.
- `timeout 240s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` passed with 0 errors and 0 warnings.
- Focused `PNetMeshRoutingUnitTests` passed after edits, 66/66 tests.
- Focused `PNetMeshTunBridgeTests` passed during combined parent verification, 4/4 tests.
- `timeout 600s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*SessionWriteReadPayloadPacket*'` completed.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Before #078 implementation, `PNetMeshChannel` defined `_relayStateLock` and used it in relay-state task swap helpers. | verified | source | `PNetMeshChannel.cs` used the lock in `GetRelayStateChangedTask` and `SignalRelayStateChanged`. |
| 2 | F | Before #078 implementation, the relay-state lock was only for wait and cancel signaling. | verified | source | The lock was isolated to relay-state `TaskCompletionSource` swap helpers. |
| 3 | F | The repository already has relay-path regression coverage in routing tests. | verified | source | Existing routing tests cover relay wait, cancel, dispose, and related pending-relay behavior. |
