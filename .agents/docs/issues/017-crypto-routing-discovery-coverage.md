---
issue: 017
date: 2026-06-30
source: coverage/readme
priority: high
status: open
research-date: 2026-06-30
research-status: partial
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

## Research

### Current State

Three-server localhost integration tests include comments for discovering over a peer and pre-connection. Server relay logic handles local delivery, known peer relay, unknown route flooding, and hop count.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README lists crypto routing and crypto discovery as a feature. | verified | source | README Features includes the claim. |
| 2 | F | Server relay logic includes known-route and unknown-route branches. | verified | source | `PNetMeshServer.ProcessControl` handles `RelayPacket` routing branches. |
| 3 | F | Existing three-server localhost test exercises relay exchange. | verified | source | `bind_three_server_to_localhost_and_relay_exchange` exists. |

## Completion Report

Pending.
