---
issue: 078
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

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `PNetMeshChannel` defines `_relayStateLock` and uses it in relay-state task swap helpers. | verified | source | `PNetMeshChannel.cs` uses the lock in `GetRelayStateChangedTask` and `SignalRelayStateChanged`. |
| 2 | F | The relay-state lock is only for wait and cancel signaling. | verified | source | The lock is isolated to relay-state `TaskCompletionSource` swap helpers. |
| 3 | F | The repository already has relay-path regression coverage in routing tests. | verified | source | Existing routing tests cover relay wait, cancel, dispose, and related pending-relay behavior. |
