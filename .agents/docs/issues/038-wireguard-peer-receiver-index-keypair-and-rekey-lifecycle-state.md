---
issue: 038
date: 2026-07-01
source: wireguard/transport
priority: high
status: completed
research-status: complete
research-date: 2026-07-01
terminal-state: completed
split-status: child
parent-issue: 035
assumptions-date: 2026-07-01
completion-date: 2026-07-01
commits: [6a37478e0ec727c9ac7bd63949f828223b2a8f6e]
brief: "description+playbook"
views:
  enrich: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 038 - Implement WireGuard Peer, Receiver-Index, Keypair, And Rekey Lifecycle State

## Description

Implement the WireGuard peer and key lifecycle state needed to manage transport sessions.

## Playbook

- `Peer table`: keep per-peer state and receiver-index lookup consistent with transport delivery.
- `Keypairs`: manage current, previous, and next keypairs across the WireGuard lifecycle.
- `Lifecycle`: model replay window, keepalive, rekey, reject timers, and related counters.

## Research

WireGuard docs and `wireguard-go` show current/previous/next keypair rotation, receiver-index remapping, and lifecycle timers/counters. The local implementation only has transport-level counters and replay tracking, so the peer/state-machine slice is still missing.

## Scope

- In scope: peer table, receiver-index map, keypair lifecycle, replay window state, timer/counter bookkeeping, tests.
- Out of scope: packet framing, crypto profile selection, cookie gating, raw plaintext exposure, IP packet helpers, external interop harness.

## Acceptance Criteria

- Peer lookup resolves receiver indexes to the expected peer state.
- Current, previous, and next keypair transitions behave as WireGuard expects.
- Replay window and lifecycle timers/counters are covered by tests.

## Parent Tracking

- Parent: #035
- Extracted scope: peer state, receiver-index lookup, and key lifecycle management.
- Standalone reason: this slice can be verified without changing packet framing or the decrypt boundary.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Peer table, receiver-index lookup, and keypair lifecycle belong together in one state slice. | verified | source | The user grouped those concerns into one child issue. |
| 2 | F | Keepalive, rekey, reject timers, and replay-window counters are transport state, not packet parsing. | verified | source | The user named those timers and counters in the lifecycle slice. |

## Completion Report

Completed in `6a37478e0ec727c9ac7bd63949f828223b2a8f6e`.

- Added WireGuard peer table state with public-key lookup, local receiver-index lookup, and current/previous/next keypair rotation.
- Added lifecycle constants, keepalive/rekey/reject timer checks, replay-window tracking, and send/receive counter bookkeeping.
- Registered completed WireGuard handshakes into the peer table and linked established transports to their keypair state.
- Added regression coverage for receiver-index lookup, keypair rotation, lifecycle counters/timers, replay rejection, and handshake registration.
- Verification passed: initial focused test failed on missing #038 types, focused #038 tests passed, `PNetMeshProtocolTest` passed, Release build passed, scoped whitespace passed, and `git diff --check` passed. Full unit verification still hits known gated issue #052 only.

## Resolving Commits

- `6a37478e0ec727c9ac7bd63949f828223b2a8f6e` - add WireGuard peer lifecycle state
