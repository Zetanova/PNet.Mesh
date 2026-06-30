---
issue: 009
date: 2026-06-30
source: e2e/coverage
priority: high
status: gated
split-status: parent
terminal-state: gated
gate: "Close only after child issues 021, 022, 023, 024, and 025 are completed or explicitly superseded."
gate-depends:
  - 021
  - 022
  - 023
  - 024
  - 025
gate-reason: "Tracking parent waits for fine-grained child issues"
ungate-when: "All child issues are completed"
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
| #021 | Direct peer payload exchange | open | Standalone direct-peer e2e scenario |
| #022 | Bootstrap discovery through a peer | open | Discovery path before payload exchange |
| #023 | Multi-hop relay route delivery | open | Separate route-path coverage |
| #024 | Restart and rejoin recovery | open | Non-bootstrap node restarts safely |
| #025 | Invalid key or route failure | open | Negative path without accepted payloads |

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

- 2026-06-30: Marked ready because the current unit-test surface already exposes router, session, and channel seams for deterministic doubles. Evidence: `PNetMeshServer.cs`, `PNetMeshSession.cs`, `PNetMeshChannel.cs`, `PNetMeshRouter.cs`, `PNetMeshServerTests.cs`.

## Completion Report

Pending.
