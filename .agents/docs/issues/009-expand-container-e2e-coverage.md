---
issue: 009
date: 2026-06-30
source: e2e/coverage
priority: high
status: open
research-date: 2026-06-30
research-status: none
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

## Research

### Open Questions

- Decide the minimum scenario matrix that should block CI.
- Determine whether test-node should expose structured readiness/events instead of relying on log parsing.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The current e2e script asserts only a narrow expected route. | verified | source | `expected_routes` includes node21/node20 ping-pong checks. |
| 2 | I | Structured test-node events would make e2e assertions less brittle than log substring matching. | unverified | internal | Needs design and implementation assessment. |

## Completion Report

Pending.
