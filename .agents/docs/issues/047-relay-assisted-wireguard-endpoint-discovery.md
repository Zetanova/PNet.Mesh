---
issue: 047
date: 2026-07-01
source: wireguard/relay
priority: high
status: ready
research-status: complete
research-date: 2026-07-01
terminal-state: ready
gate: "Wait for the relay path and authenticated endpoint/keypair state."
gate-depends:
  - 038
  - 045
  - 046
gate-reason: "Endpoint discovery depends on the relay path and authenticated peer endpoint state; direct promotion must wait for an authenticated response."
gate-last-checked: 2026-07-01
gate-status: cleared
assumptions-date: 2026-07-01
brief: "description+playbook"
views:
  enrich: "description+playbook+related-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 047 - Relay-Assisted WireGuard Endpoint Discovery

## Description
Implement relay-assisted endpoint discovery so node1 can initially reach a `wireguard-go` node3 peer through relay node2, learn node3's observed UDP endpoint from node2, probe node3 directly, and promote the session to a direct path only after receiving an authenticated direct WireGuard response.

The relay path must stay active until direct connectivity is confirmed, and node1 must fall back to relay if the direct candidate fails.

## Playbook
- `Initial relay`: node1 sends WireGuard handshake or data to node3 through node2 using #045/#046 relay behavior.
- `Endpoint hint`: node2 reports the node3 UDP endpoint observed from the relayed response as routing metadata, not as trusted security state.
- `Direct probe`: node1 sends a valid authenticated WireGuard packet directly to the hinted node3 endpoint.
- `Promotion`: node1 switches to direct only after it receives and authenticates a direct response from node3.
- `Fallback`: node1 keeps the relay mapping alive during probing and continues through node2 if the direct candidate times out or fails authentication.
- `Expiry`: endpoint hints and probe attempts expire deterministically to avoid stale direct-path state.

## Research

Relay-assisted endpoint discovery is correctly scoped as a follow-on to the relay and peer-state work: `wireguard-go` updates the endpoint only after authenticated handshake or replay-validated transport traffic, so any direct-path promotion must be gated on authenticated direct responses. The relay hint itself remains untrusted routing metadata.

## Related Tracking
- Parent: #035 WireGuard-compatible transport mode.
- Related: #045 PNet relay to WireGuard peer interop.
- Related: #046 shared-port relay registration and WireGuard packet demux.
- Related: #050 relay audit and diagnostics hardening.
- Related: #038 WireGuard peer, endpoint, and key lifecycle state.

## Scope
- In scope: endpoint hint message from node2 to node1, node1 direct-path probe state, authenticated direct-path promotion, relay fallback, timeout/expiry, and e2e tests.
- Out of scope: ICE/STUN candidate gathering beyond the node3 endpoint observed by node2, source-address spoofing, promoting before authentication, custom behavior required from stock `wireguard-go`.

## Acceptance Criteria
- Node2 can include node3's observed UDP endpoint in a side-channel relay hint to node1.
- Node1 treats endpoint hints as untrusted routing candidates and never as proof of peer identity.
- Node1 sends a direct WireGuard probe to the hinted node3 endpoint while preserving the relay path.
- Node1 promotes the peer endpoint to direct only after an authenticated direct response from node3.
- Node1 falls back to relay when the direct candidate times out, fails authentication, or becomes stale.
- Tests cover successful promotion, failed direct probe fallback, stale hint expiry, and no promotion from unauthenticated packets.

## Assumptions
| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Node1 should be able to learn node3's observed UDP endpoint from node2 after a relayed exchange. | verified | source | The user requested learning node3's endpoint from the relayed handshake response or a side packet. |
| 2 | F | Node1 should try direct communication with node3 after receiving the endpoint hint. | verified | source | The user requested that node1 send later packets directly to node3 after learning the address. |
| 3 | F | Direct-path promotion must require authenticated WireGuard traffic from node3. | verified | source | `wireguard-go` updates endpoint state only after authenticated handshake processing or successfully decrypted, replay-validated transport packets. |
| 4 | F | The endpoint observed by node2 may not be reachable from node1. | verified | source | The learned endpoint is routing metadata, not proof of reachability; the relay path must stay active until direct connectivity is confirmed. |
| 5 | F | The relay path should remain active until direct connectivity is confirmed. | verified | logical | This follows from #045 relay fallback behavior and the fact that the learned endpoint is only routing metadata until direct connectivity is confirmed. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-01 | `gate-depends: [038, 045, 046]` | source | ready | #038, #045, and #046 are completed, so #047 is implementation-ready. |

## Validation History

- 2026-07-01: dependency gate cleared by #046; remaining dependency gate #045 keeps #047 gated.
- 2026-07-01: dependency gate cleared by #045; #047 is now ready.
