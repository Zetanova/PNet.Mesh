---
issue: 046
date: 2026-07-01
source: wireguard/relay
priority: high
status: completed
research-status: complete
research-date: 2026-07-01
terminal-state: completed
gate: "Wait for the WireGuard MAC/framing, receiver-index, and cookie/DoS foundations."
gate-depends:
  - 036
  - 037
  - 038
  - 039
gate-reason: "Shared-port registration and demux depend on BLAKE2s MAC helpers, packet framing, receiver-index state, and cookie-gate behavior."
gate-last-checked: 2026-07-01
gate-status: cleared
assumptions-date: 2026-07-01
completion-date: 2026-07-01
commits: [c9efebaa5ad7bbb82fde4270dd0d3e8a5873e0b0]
brief: "description+playbook"
views:
  enrich: "description+playbook+related-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 046 - Shared-Port WireGuard Relay Registration And Demux

## Description
Implement a TURN-like shared UDP relay mode where `PNet.Mesh` node1 registers a WireGuard relay lease on node2, and node2 demultiplexes inbound WireGuard packets on a shared UDP port back to the registered PNet node without decrypting WireGuard payloads.

The relay should use MAC1-based registered-public-key matching only for handshake classification, then learn a receiver-index fast path for later transport packets.

## Playbook
- `Registration`: node1 authenticates to node2 and registers a relay lease containing node1's WireGuard static public key, PNet return route, expiry, and optional allowed remote endpoint filters.
- `Shared listener`: node2 receives WireGuard-compatible UDP packets for many registered nodes on one UDP port.
- `Handshake demux`: for handshake packets with MAC1, node2 tests registered public-key candidates, routes exactly one match, and drops zero-match or ambiguous-match packets.
- `Fast path`: node2 learns `remote_endpoint + receiver_index -> registered node` mappings from routed handshakes and later routes transport packets by receiver index without MAC scanning.
- `MAC2`: do not use MAC2 as the primary routing signal; node2 may pass it through but should not require node1 cookie secrets.
- `Opaque relay`: node2 never decrypts WireGuard transport plaintext and never owns the node1-to-remote WireGuard session.
- `Defense`: rate-limit MAC1 scans, cap registrations, expire leases and learned indexes, and reject malformed packet sizes before expensive work.

## Research

WireGuard protocol docs and `wireguard-go` show the relay needs BLAKE2s MAC helpers, packet framing, receiver-index/keypair state, and cookie/DoS gate behavior before the shared-port demux can be made correct. MAC1 can classify handshakes without decrypting, transport packets lack MAC1/MAC2, and MAC2 is a DoS cookie signal rather than a routing key.

## Related Tracking
- Parent: #035 WireGuard-compatible transport mode.
- Related: #045 PNet relay to WireGuard peer interop.
- Related: #047 relay-assisted WireGuard endpoint discovery and direct-path promotion.
- Related: #050 relay audit and diagnostics hardening.
- Related: #037 WireGuard packet framing and packet type parsing.
- Related: #039 WireGuard cookie reply and DoS gate behavior.

## Scope
- In scope: shared-port relay registration, lease renewal and expiry, public-key candidate table, MAC1 handshake demux, receiver-index fast-path table, endpoint filters, rate limits, unit tests, and e2e coverage with #045.
- Out of scope: dedicated-port relay allocation, node2 decrypting WireGuard traffic, node2 acting as the WireGuard endpoint, source-address spoofing, custom PNet protobuf payloads for stock WireGuard peers.

## Acceptance Criteria
- Node1 can register, renew, and release a shared-port WireGuard relay lease on node2.
- Node2 stores node1's WireGuard static public key, PNet return route, expiry, and optional allowed remote endpoint filters.
- Node2 routes inbound handshake packets by MAC1 candidate matching against registered node public keys.
- Node2 rejects malformed handshakes, zero MAC1 matches, and ambiguous MAC1 matches.
- Node2 learns receiver-index fast-path mappings after routing a handshake.
- Node2 routes later transport packets by `remote_endpoint + receiver_index` without MAC1 scanning.
- Node2 expires leases and learned receiver-index mappings deterministically.
- Tests prove MAC1 scan fallback, receiver-index fast path, endpoint filtering, expiry, and DoS/rate-limit behavior.

## Assumptions
| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Shared-port relay registration is the desired relay mode instead of dedicated per-node UDP ports. | verified | source | The user selected shared port and proposed registered-node public-key matching plus fast-path candidates. |
| 2 | F | MAC1 can classify WireGuard handshake packets for registered node public keys without decrypting payloads. | verified | source | WireGuard handshakes place MAC1 outside encrypted handshake fields, and `wireguard-go` checks MAC1 before consuming handshake content. |
| 3 | F | WireGuard transport packets do not carry MAC1/MAC2 and therefore require a learned receiver-index fast path. | verified | source | WireGuard transport packet layout is type, receiver index, counter, encrypted payload; `wireguard-go` `MessageTransport` has no MAC fields. |
| 4 | F | MAC2 should not be the primary routing signal for the shared-port relay. | verified | source | WireGuard uses MAC2 only for cookie/load DoS gating and may send zero MAC2 when no valid cookie exists. |
| 5 | F | Node2 can remain WireGuard-aware only at packet metadata level and still avoid decrypting or terminating sessions. | verified | logical | #045 requires opaque relay behavior, while this issue routes by clear packet metadata only. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-01 | `gate-depends: [036, 037, 038, 039]` | source | ready | #036, #037, #038, and #039 are completed, so #046 is implementation-ready. |

## Validation History

- 2026-07-01: dependency gate cleared by #036; remaining dependency gates #037, #038, and #039 keep #046 gated.
- 2026-07-01: dependency gate cleared by #037; remaining dependency gates #038 and #039 keep #046 gated.
- 2026-07-01: dependency gate cleared by #038; remaining dependency gate #039 keeps #046 gated.
- 2026-07-01: dependency gate cleared by #039; #046 is now ready.

## Completion Report

Completed in `c9efebaa5ad7bbb82fde4270dd0d3e8a5873e0b0`.

- Added `PNetMeshWireGuardRelayRegistry` with relay lease registration, renewal, release, expiry, and max-lease cap.
- Added MAC1 handshake demux against registered public keys with explicit zero-match, ambiguous-match, endpoint-filter, malformed-packet, and rate-limit results.
- Added learned `remote_endpoint + receiver_index` fast-path routing for later transport packets without MAC1 scanning.
- Kept relay routing opaque: the registry uses packet metadata and MAC1 only, never decrypts WireGuard payloads.
- Verification passed: initial focused compile failed on missing #046 registry types, focused #046 tests passed 6/6, Release build passed, scoped whitespace passed, `git diff --check` passed, and the full unit command passed 132/132.

## Resolving Commits

- `c9efebaa5ad7bbb82fde4270dd0d3e8a5873e0b0` - add WireGuard shared relay registry
