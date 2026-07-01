---
issue: 017
date: 2026-06-30
source: coverage/readme
priority: high
status: gated
split-status: parent
terminal-state: gated
gate: "Close only after child issues 032 and 033 are completed or explicitly superseded."
gate-depends:
  - 032
  - 033
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

# 017 - Cover Crypto Routing And Discovery

## Description

Implement and test the README feature claim for crypto routing and crypto discovery, including address derivation from public keys, relay routing, endpoint learning, and discovery through already-connected peers.

This is a parent tracking issue. Implement child issues, not this parent directly.

## Playbook

- `Crypto address`: public keys derive stable mesh addresses used by router/session logic.
- `Discovery`: peer without direct endpoint can discover a route through a connected peer.
- `Relay routing`: unknown route floods within hop budget; known route uses the selected channel.
- `Loop/replay guard`: route history and relay packet tracker suppress loops and duplicates.

## Scope

- Add deterministic router/session tests for address, route, hop, and duplicate behavior.
- Add integration/e2e scenarios for discovery through a bootstrap peer and multi-hop route delivery.
- Expose enough diagnostics to assert the actual route, not just a final pong.

## Acceptance Criteria

- Direct and discovered peer routes both have passing tests.
- Multi-hop relay has e2e coverage.
- Duplicate relay packet suppression has unit coverage.
- README claim maps to named tests.

## Tracking

| Child | Scope | Status | Notes |
|-------|-------|--------|-------|
| #032 | Routing and discovery behavior coverage | completed | Direct/discovered routes plus deterministic address coverage |
| #033 | Route-path observability and diagnostics | open | Assert the actual route, not only the final payload |

## Residual Scope
none

## Research

### Current State

Three-server localhost integration tests include comments for discovering over a peer and pre-connection. Server relay logic handles local delivery, known peer relay, unknown route flooding, and hop count.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README lists crypto routing and crypto discovery as a feature. | verified | source | README Features includes the claim. |
| 2 | F | Server relay logic includes known-route and unknown-route branches. | verified | source | `PNetMeshServer.ProcessControl` handles `RelayPacket` routing branches. |
| 3 | F | Existing three-server localhost test exercises relay exchange. | verified | source | `bind_three_server_to_localhost_and_relay_exchange` exists. |

## Enrichment History

- 2026-06-30: Marked ready after confirming relay/discovery behavior and route assertions already exist in source and localhost tests. Evidence: `PNetMeshServer.cs`, `PNetMeshServerTests.cs`.

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-01 | `gate-depends: [032, 033]` | source | blocked | #032 is completed, but #033 remains open, so the parent stays gated. |

## Validation History

- 2026-07-01: dependency gate cleared by #032; remaining dependency gate #033 keeps #017 gated.

## Completion Report

Pending.
