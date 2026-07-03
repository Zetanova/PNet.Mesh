---
issue: 084
date: 2026-07-03
source: refactor-audit
priority: medium
status: completed
terminal-state: completed
research-status: complete
research-date: 2026-07-03
assumptions-date: 2026-07-03
completed-date: 2026-07-03
completed-commits:
  - 4cb0090c463832e9b435d141830514303a641ced
brief: "description+scope+acceptance-criteria+assumptions"
views:
  fix: "description+scope+acceptance-criteria+proposed-refactor+verification+assumptions"
  enrich: "description+scope+acceptance-criteria+proposed-refactor+verification+assumptions"
  complete: "description+completion-report+resolving-commits"
---

# 084 - Refactor PNetMeshChannel State Ownership

## Description

Source proposal: `RP-002`.

`PNetMeshChannel` now uses atomic relay-state signaling, but adjacent mutable state still has no component-level `multi-threading:` rationale. The channel is touched by public send and relay APIs, session callbacks, relay and control loops, cancellation callbacks, and disposal.

## Scope

- In scope: `src/PNet.Mesh/PNetMeshChannel.cs` state ownership comments and publication of `_disposed`, `_currentSession`, and `_sessions`.
- Keep the completed #078 `_relayStateChanged` `Volatile.Read` / `Interlocked.Exchange` behavior intact.
- Out of scope: session mailbox refactor, TUN connect memoization, routing semantics, and wire protocol changes.

## Acceptance Criteria

- `PNetMeshChannel` has a compact `multi-threading:` rationale naming the concrete concurrent callers.
- Reads and writes of channel dispose/session state use either explicit volatile/atomic publication or one serialized owner path.
- Relay waiters, relay cancellation, dispose wakeup, and later-routable-session behavior remain covered and passing.
- No broad concurrent collection rewrite replaces the channel's existing ownership model without evidence.

## Proposed Refactor

Add the concurrency rationale near the guarded state. Then tighten `_disposed`, `_currentSession`, and `_sessions` publication using explicit `Volatile.Read` / `Volatile.Write` patterns or move the remaining decisions behind the channel's serialized control/relay loops.

## Verification

```bash
dotnet build PNet.Mesh.sln -c Release --no-restore
dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none
dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh/PNetMeshChannel.cs src/PNet.Mesh.UnitTests/PNetMeshRoutingUnitTests.cs --no-restore --verify-no-changes --verbosity minimal
```

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `PNetMeshChannel` stores channel fields, task fields, session state, relay-state TCS, and `_disposed` without a component-level `multi-threading:` rationale. | verified | source | `src/PNet.Mesh/PNetMeshChannel.cs:20-32`. |
| 2 | F | Disposal mutates `_disposed`, `_currentSession`, and channel fields while relay processing reads dispose/session state. | verified | source | `src/PNet.Mesh/PNetMeshChannel.cs:91-111` and `src/PNet.Mesh/PNetMeshChannel.cs:213-270`. |
| 3 | F | Completed issue #078 only scoped relay-state TCS atomic signaling. | verified | source | `.agents/docs/issues/078-pnetmeshchannel-relay-state-atomic-signaling.md` lists only `_relayStateChanged` removal and atomic signaling. |

## Completion Report

Resolved in `4cb0090c463832e9b435d141830514303a641ced`.

## Resolving Commits

- `4cb0090c463832e9b435d141830514303a641ced`
