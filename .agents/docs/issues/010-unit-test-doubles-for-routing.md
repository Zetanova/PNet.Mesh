---
issue: 010
date: 2026-06-30
source: tests/unit-doubles
priority: high
status: ready
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 010 - Add Unit Test Doubles For Routing

## Description

Add deterministic unit or component tests with doubles around routing, session, relay, and control-channel behavior so core decisions can be exercised without Docker networking.

## Playbook

- `Router decisions`: tests cover insert/update/lookup, unknown destination, route freshness, and duplicate relay packet handling.
- `Session decisions`: tests cover relay packet encode/decode, hop decrement, route append, candidate exchange mapping, ack flushing, and retransmit triggers.
- `Channel/control decisions`: tests cover failure behavior when channel writes fail or routes cannot be found.

## Scope

- Identify small seams that let tests observe outbound control messages without live sockets.
- Prefer in-memory channels and deterministic clocks over sleeps.
- Keep public API changes minimal; do not introduce broad abstractions unless tests need them.

## Acceptance Criteria

- Relay hop-count and route-loop behavior has deterministic tests.
- Candidate exchange mapping has deterministic tests.
- Duplicate relay packet suppression has deterministic tests.
- Existing localhost integration tests remain green.

## Research

### Code References

- `src/PNet.Mesh/PNetMeshRouter.cs`
- `src/PNet.Mesh/PNetMeshSession.cs`
- `src/PNet.Mesh/PNetMeshChannel.cs`
- `src/PNet.Mesh/PNetMeshServer.cs`

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Routing and relay logic currently lives across server, session, channel, and router classes. | verified | source | Relevant code paths are in `PNetMeshServer`, `PNetMeshSession`, `PNetMeshChannel`, and `PNetMeshRouter`. |
| 2 | I | Some test seams may be required to cover relay decisions without sockets. | verified | source | `PNetMeshServer`, `PNetMeshSession`, `PNetMeshChannel`, and `PNetMeshRouter` already expose internal seams for deterministic tests. |

## Enrichment History

- 2026-06-30: Marked ready after confirming internal seams already support in-memory relay/session/channel tests without sockets. Evidence: `PNetMeshServer.cs`, `PNetMeshSession.cs`, `PNetMeshChannel.cs`, `PNetMeshRouter.cs`.

## Completion Report

Pending.
