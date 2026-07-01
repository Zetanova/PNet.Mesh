---
issue: 009
date: 2026-06-30
source: e2e/coverage
priority: high
status: completed
terminal-state: completed
completion-date: 2026-06-30
commits: [61af492, 56a70ff, 84bb53b, 6a02e9d, 7c09bd6]
split-status: parent
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 009 - Expand Container E2E Coverage

## Description

Add scenario-level container coverage beyond the current single compose smoke route so PNet.Mesh has observable confidence for peer communication, routing, discovery, restarts, and failure behavior.

This is a parent tracking issue. Implement child issues, not this parent directly.

## Playbook

- `Direct peer`: two nodes on one network exchange bidirectional payloads.
- `Bootstrap discovery`: nodes learn peers through a rendezvous node before exchanging payloads.
- `Multi-hop route`: peers on separated network segments communicate through relay paths.
- `Restart recovery`: one non-bootstrap node restarts and rejoins without breaking unrelated peers.
- `Negative path`: invalid PSK/public key/corrupt route does not produce accepted payload delivery.

## Scope

- Build scenarios on top of the Testcontainers harness from #007.
- Prefer short deterministic runs over long sleeps.
- Add explicit assertions for expected message counts and route pairs.
- Keep expensive scenarios separate or tagged so CI can choose smoke versus full e2e.

## Acceptance Criteria

- At least one scenario covers direct peer-to-peer communication.
- At least one scenario covers multi-hop crypto routing and discovery.
- At least one scenario covers a negative security or route failure.
- CI/docs distinguish smoke and fuller e2e commands.

## Tracking

| Child | Scope | Status | Notes |
|-------|-------|--------|-------|
| #021 | Direct peer payload exchange | completed | Completed in 61af492 |
| #022 | Bootstrap discovery through a peer | completed | Completed in 56a70ff |
| #023 | Multi-hop relay route delivery | completed | Completed in 84bb53b |
| #024 | Restart and rejoin recovery | completed | Completed in 6a02e9d |
| #025 | Invalid key or route failure | completed | Completed in 7c09bd6 |

## Residual Scope
none

## Research

### Open Questions

- Decide the minimum scenario matrix that should block CI.
- Determine whether test-node should expose structured readiness/events instead of relying on log parsing.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The current e2e script asserts only a narrow expected route. | verified | source | `expected_routes` includes node21/node20 ping-pong checks. |
| 2 | I | Structured test-node events would make e2e assertions less brittle than log substring matching. | verified | source | `NodeService` currently exposes only text logs for start/stop/ping/pong, so the current assertion surface is still log text. |

## Enrichment History

- 2026-06-30: Removed completed child issue #025 from the active gate after 7c09bd6; all child scenario gates are complete and the parent tracker is now completed.
- 2026-06-30: Removed completed child issue #024 from the active gate after 6a02e9d; parent remains gated on #025.
- 2026-06-30: Removed completed child issue #023 from the active gate after 84bb53b; parent remains gated on #024 and #025.
- 2026-06-30: Removed completed child issue #022 from the active gate after 56a70ff; parent remains gated on #023, #024, and #025.
- 2026-06-30: Removed completed child issue #021 from the active gate after 61af492; parent remains gated on #022, #023, #024, and #025.
- 2026-06-30: Marked ready because the current unit-test surface already exposes router, session, and channel seams for deterministic doubles. Evidence: `PNetMeshServer.cs`, `PNetMeshSession.cs`, `PNetMeshChannel.cs`, `PNetMeshRouter.cs`, `PNetMeshServerTests.cs`.

## Completion Report

Completed in `7c09bd6` after the final child scenario closed.

- Cleared the last active dependency gate by marking child issue #025 completed in the tracking table.
- Promoted the parent tracker to completed because child issues #021 through #025 are all complete.
- Preserved the existing child coverage history and recorded the final child commit trail for the parent closeout.

## Resolving Commits

- `61af4927ae94d686a2e1597bb0b6df17be8ad0d5` - add direct peer Testcontainers coverage
- `56a70ff0c1da20428bd2ccfa41d6267e3c3155b4` - add bootstrap discovery Testcontainers coverage
- `84bb53b2ecbcd4e9f5f4aaad6518c2f4d66f42be` - add multi-hop route Testcontainers coverage
- `6a02e9de5d17f18914341a2ddf5fbfb164464fcf` - add restart recovery Testcontainers coverage
- `7c09bd63376e8929d100cd79497b6ef09d6f3fd5` - add invalid PSK Testcontainers coverage
