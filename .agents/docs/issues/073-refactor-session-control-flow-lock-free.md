---
issue: 073
date: 2026-07-02
source: performance/refactor
priority: high
status: completed
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
split-status: parent
terminal-state: completed
completed-date: 2026-07-02
completion-commits:
  - e480ba9
  - a8c2a6b
  - c89ada0
brief: "description+related-issues+playbook+scope+out-of-scope+benchmark-plan+acceptance-criteria+tracking+residual-scope+assumptions"
views:
  enrich: "description+related-issues+playbook+scope+out-of-scope+benchmark-plan+acceptance-criteria+tracking+residual-scope+assumptions"
  fix: "description+related-issues+playbook+scope+out-of-scope+benchmark-plan+acceptance-criteria+tracking+residual-scope+assumptions"
  complete: "description+completion-report"
---

# 073 - Refactor Session Control Flow Lock Free

## Description

`PNetMeshSession` currently uses multiple locks to protect state shared by server receive processing, channel send and relay processing, timer callbacks, and disposal. Refactor the control flow so session state is owned by one serialized execution path instead of guarded piecemeal with locks.

Prefer channel-owned state, immutable snapshots, `Volatile.Read`, and `Interlocked.Exchange` over `lock` and `SemaphoreSlim`. A larger control-flow refactor is acceptable when it removes the shared-state model instead of just replacing locks with concurrent containers.

This is a parent tracking issue. Implement the child issues below, not this parent directly.

## Related Issues

- `#055`: core protocol microbenchmarks.
- `#056`: macro throughput and latency benchmarks.
- `#057`: benchmark baseline and regression policy.
- `#072`: .NET 10 memory and `CommunityToolkit.HighPerformance` optimization pass.

## Playbook

- `Baseline first`: capture current micro and macro benchmark results before source edits.
- `Single owner`: move session mutations behind one per-session mailbox or equivalent single-reader actor.
- `No lock-for-lock rewrite`: avoid `ConcurrentDictionary` or `ConcurrentQueue` as the primary fix for multi-field session invariants.
- `Atomic only for isolated swaps`: use `Volatile.Read` and `Interlocked.Exchange` for standalone signal or memoized-reference swaps.
- `Tests before and after`: run targeted regression coverage before and after the refactor.
- `Benchmark-driven`: accept changes only when microbenchmarks show no meaningful regression and macro benchmarks confirm no throughput or allocation damage.

## Scope

- Split the session refactor into child issues #077, #078, and #079.
- Keep the session mailbox, relay-state atomics, and TUN connect memoization slices independent.
- Leave the benchmark and regression criteria on the child issues.

## Out Of Scope

- Rewriting the wire protocol, ACK semantics, retransmission semantics, or routing behavior.
- Introducing broad concurrent collections as a substitute for session ownership.
- Making TUN benchmark thresholds hard blockers before the benchmark policy has stable repeated baselines.

## Benchmark Plan

- Micro baseline and comparison:

```bash
timeout 900s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*'
```

- Focused session micro benchmark during iteration:

```bash
timeout 300s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*SessionWriteReadPayloadPacket*'
```

- Macro baseline and comparison:

```bash
timeout 180s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --macro all --payload 128 --warmup 00:00:05 --duration 00:00:30
```

- Optional privileged TUN comparison when host preflight passes:

```bash
timeout 900s scripts/bench-tun-comparison.sh --output-dir artifacts/benchmarks/tun-comparison/session-lock-refactor
```

## Acceptance Criteria

- Child issues #077, #078, and #079 are completed or explicitly superseded.
- The parent remains gated until all three child issues are complete.
- Residual scope remains `none` after the child issues land.

## Tracking

| Child | Scope | Status | Notes |
|-------|-------|--------|-------|
| #077 | PNetMeshSession single-owner mailbox refactor | completed | `e480ba9` serializes mutable session state behind one owner and keeps callbacks off the owner gate. |
| #078 | PNetMeshChannel relay-state atomic signaling | completed | `a8c2a6b` replaces relay-state locking with atomic TCS signaling. |
| #079 | PNetMeshTunBridge peer connect memoization | completed | `c89ada0` replaces first-connect serialization with async memoization. |

## Residual Scope
`none`

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | `PNetMeshSession` currently has separate locks for open packet, retransmit buffer, receive state, remote ACK, endpoint discovery, and control queue state. | verified | source | `PNetMeshSession.cs` defines `_openPacketLock`, `_retransBufferLock`, `_receiveStateLock`, `_remoteAckLock`, `_endpointDiscoveryLock`, and `_controlQueueLock`. |
| 2 | F | `PNetMeshSession` is currently shared by server receive handling, channel send/relay processing, timers, and dispose paths. | verified | source | `PNetMeshServer.ProcessControl` calls `TryReadMessage`; `PNetMeshChannel.ProcessControl` calls `WritePayload`; relay processing calls `WriteRelay`; timer callbacks enqueue retransmit and ACK flush work. |
| 3 | F | `PNetMeshChannel` has a small relay-state lock around a `TaskCompletionSource` swap. | verified | source | `PNetMeshChannel.cs` defines `_relayStateLock` and uses it in `GetRelayStateChangedTask` and `SignalRelayStateChanged`. |
| 4 | F | `PNetMeshTunBridge.PeerState` uses `SemaphoreSlim` only to serialize first channel connect and cache population. | verified | source | `PNetMeshTunBridge.cs` defines `_connectLock` and uses it in `GetChannelAsync`. |
| 5 | F | The repository has concrete micro and macro benchmark commands for protocol/session and in-memory or UDP loopback scenarios. | verified | source | `README.md` and `.agents/docs/benchmarks/regression-policy.md` document BenchmarkDotNet and `--macro all` commands. |
| 6 | R | A per-session owner path can remove most session locks without changing covered protocol behavior. | verified | test | `e480ba9` passed full unit coverage and before/after micro and macro benchmark gates. |
| 7 | R | `Volatile.Read` plus `Interlocked.Exchange` is sufficient for relay-state signal swapping. | verified | test | `a8c2a6b` passed targeted relay-state, cancellation, dispose, build, and unit checks. |
| 8 | R | Lock-free async connect memoization can replace the TUN bridge semaphore without increasing duplicate connects or complexity. | verified | test | `c89ada0` passed concurrent peer reader/writer startup coverage and TUN unit checks. |

## Completion Report

All child issues are complete:

| Child | Commit | Evidence |
|---|---|---|
| #077 | e480ba9 | Full unit suite 194/194, focused session micro benchmark, and in-memory/UDP macro benchmark comparison passed the benchmark gate. |
| #078 | a8c2a6b | Relay-state atomic signaling tests and full unit coverage passed. |
| #079 | c89ada0 | TUN bridge connect memoization tests and TUN unit coverage passed. |

Residual scope remains `none`; no follow-up issue was required for this parent.
