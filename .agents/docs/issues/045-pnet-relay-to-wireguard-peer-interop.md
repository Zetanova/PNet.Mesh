---
issue: 045
date: 2026-07-01
source: wireguard/relay
priority: high
status: gated
research-status: complete
research-date: 2026-07-01
terminal-state: gated
gate: "Wait for the WireGuard-compatible transport, raw plaintext, IP helper, and direct interop child issues."
gate-depends:
  - 036
  - 037
  - 038
  - 039
  - 040
  - 041
  - 042
gate-reason: "Relay interop depends on the unfinished WireGuard-compatible transport and interop children."
gate-last-checked: 2026-07-01
gate-status: blocked
assumptions-date: 2026-07-01
brief: "description+playbook"
views:
  enrich: "description+playbook+related-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 045 - PNet Relay To WireGuard Peer Interop

## Description
Implement and test a relay path where `PNet.Mesh` node1 sends WireGuard-compatible encrypted UDP datagrams through `PNet.Mesh` node2 to a `wireguard-go` node3 peer, and node2 relays node3's encrypted UDP responses back to node1.

This proves that PNet can carry an opaque WireGuard session over a PNet relay without requiring node2 to decrypt or own the WireGuard peer session.

## Playbook
- `Topology`: run node1 as the WireGuard session owner, node2 as the PNet relay, and node3 as `wireguard-go` or equivalent.
- `Outbound relay`: node1 sends encrypted WireGuard UDP datagrams to node2, and node2 forwards those datagrams to node3.
- `Return relay`: node3 replies to node2's observed UDP endpoint; node2 must map the response back to node1 and forward it.
- `Opaque transport`: node2 treats WireGuard datagrams as opaque bytes and does not decrypt, parse IP plaintext, or terminate the WireGuard session.
- `IP payload`: node1 must craft valid IPv4 or IPv6 plaintext packets acceptable to node3's WireGuard peer configuration.
- `Assertions`: verify handshake, bidirectional encrypted packet exchange, node1 plaintext decrypt, and relay mapping cleanup.

## Research

Primary `wireguard-go` receive/send flow shows that a normal UDP relay makes node3 observe node2 as the source endpoint, and node3 sends the handshake response through its peer send path back toward that observed endpoint. Keep the acceptance criteria focused on proving that node2 relays node3's response back to node1 and that node1 decrypts the returned transport packet.

## Related Tracking
- Parent: #035 WireGuard-compatible transport mode.
- Related: #046 shared-port relay registration and WireGuard packet demux.
- Related: #047 relay-assisted WireGuard endpoint discovery and direct-path promotion.
- Related: #050 relay audit and diagnostics hardening.
- Related: #040 raw decrypted WireGuard transport plaintext bytes.
- Related: #041 IPv4/IPv6 packet read/create helpers.
- Related: #042 direct `wireguard-go` interoperability test.

## Scope
- In scope: relay mapping for opaque WireGuard UDP datagrams, node1-to-node3 forwarding through node2, node3-to-node1 return forwarding through node2, e2e Testcontainers or equivalent topology, focused observability.
- Out of scope: making node2 a WireGuard endpoint, decrypting relay traffic on node2, source-address spoofing, direct node3 response to node1 without relay, custom PNet protobuf payload support for stock `wireguard-go`.

## Acceptance Criteria
- E2E topology starts node1, node2, and node3 with deterministic keys and endpoints.
- Node1 and node3 complete a WireGuard-compatible handshake through node2.
- Node1 sends an encrypted transport packet through node2 to node3.
- Node3 replies to node2's UDP endpoint, and node2 relays the response back to node1.
- Node1 decrypts the response and recovers the expected IPv4 or IPv6 packet plaintext.
- Node2 never decrypts or parses the WireGuard transport plaintext.
- Test assertions prove relay mapping cleanup or expiry after the exchange.

## Assumptions
| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The desired relay topology is `PNet.Mesh` node1 through `PNet.Mesh` node2 to `wireguard-go` node3. | verified | source | The user requested this exact relay topology. |
| 2 | F | In a normal userspace UDP relay, node3 will reply to node2's observed UDP endpoint rather than directly to node1. | verified | source | `wireguard-go` stores the source endpoint from an authenticated handshake initiation before sending its handshake response, so a userspace UDP relay makes node2 the observed source endpoint. |
| 3 | F | Node2 should forward WireGuard datagrams opaquely and must not terminate the WireGuard session. | verified | source | The user asked for node2 to relay packets for node1 to a third WireGuard peer, not to become the peer itself. |
| 4 | F | Node1 must craft a valid IPv4 or IPv6 packet plaintext for stock `wireguard-go` node3. | verified | source | The user confirmed stock WireGuard peers require regular IP packet plaintext. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-01 | `gate-depends: [036, 037, 038, 039, 040, 041, 042]` | source | blocked | #036, #037, and #038 are completed, but #039, #040, #041, and #042 remain open, so the issue stays gated. |

## Validation History

- 2026-07-01: dependency gate cleared by #036; remaining dependency gates #037, #038, #039, #040, #041, and #042 keep #045 gated.
- 2026-07-01: dependency gate cleared by #037; remaining dependency gates #038, #039, #040, #041, and #042 keep #045 gated.
- 2026-07-01: dependency gate cleared by #038; remaining dependency gates #039, #040, #041, and #042 keep #045 gated.
