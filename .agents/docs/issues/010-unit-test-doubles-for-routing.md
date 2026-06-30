---
issue: 010
date: 2026-06-30
source: tests/unit-doubles
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-06-30
commits: [49deb32]
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

Completed in `49deb32`.

- Added deterministic xUnit coverage for router insert/update/lookup, route freshness, unknown lookup, and invalid address rejection.
- Extracted small internal `PNetMeshServer` relay decision helpers and covered duplicate relay suppression plus route-loop peer filtering without live sockets.
- Added an in-memory session relay round-trip test covering relay encode/decode, hop-count decrement, route append, payload preservation, and candidate exchange field mapping.
- Verified Release build, full unit-test command (`28` total, `0` failed/skipped/not-run), scoped whitespace formatting, and `git diff --check`.
- Testing specialist approved the patch with no blocking findings.

## Resolving Commits

- `49deb32fd59c4bb277c9bb5e2d0410d201bfbf69` - add deterministic routing unit coverage
