---
issue: 035
date: 2026-07-01
source: wireguard/transport
priority: high
status: gated
terminal-state: gated
split-status: parent
gate: "Close only after child issues #036 through #042 and #045 through #048 are completed or explicitly superseded."
gate-depends:
  - 036
  - 037
  - 038
  - 039
  - 040
  - 041
  - 042
  - 045
  - 046
  - 047
  - 048
gate-reason: "Tracking parent waits for fine-grained child issues"
research-status: none
assumptions-date: 2026-07-01
brief: "description+playbook+tracking"
views:
  enrich: "description+playbook+scope+acceptance-criteria+tracking+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+tracking+assumptions"
  complete: "description+completion-report"
---

# 035 - WireGuard-Compatible Transport Mode

## Description

Track WireGuard-compatible transport mode through handshake, transport decrypt, raw plaintext, IP packet, PNet internal frame e2e, direct interop, relay interop, shared-port relay demux, and relay-assisted endpoint discovery work.

This is a parent tracking issue. Implement child issues, not this parent directly.

## Playbook

- `Handshake`: match the WireGuard Noise and hash profile needed for transport.
- `Framing`: keep handshake, cookie, and transport packet layouts aligned with WireGuard.
- `Plaintext`: transport decrypt yields authenticated raw bytes and peer metadata, not protobuf parsing.
- `IP layer`: packet helpers sit above raw plaintext bytes.
- `Interop`: container-based testing proves direct WireGuard, native PNet, relayed, shared-port, and relay-promoted direct compatibility end to end.

## Scope

- Split transport-mode work into the eleven child issues below.
- Keep the raw decrypt boundary free of protobuf and IP parsing.
- Track the external peer harness separately from core protocol code.

## Acceptance Criteria

- Each child issue has a narrow, named scope.
- The parent stays gated until all eleven children are complete or superseded.
- The raw plaintext child remains separate from the IP-packet child.

## Tracking

| Child | Scope | Status | Notes |
|-------|-------|--------|-------|
| #036 | WireGuard Noise profile and BLAKE2s MAC/KDF paths | ready | Core crypto/profile split. |
| #037 | Packet framing and TAI64N handshake replay tracking | ready | Message sizes, headers, and replay timestamps. |
| #038 | Peer, receiver-index, keypair, and rekey lifecycle state | ready | Peer table and key lifecycle management. |
| #039 | Cookie reply and DoS gate behavior | ready | Load-shedding and endpoint-bound cookies. |
| #040 | Decrypted transport plaintext as raw payload bytes | gated | Raw authenticated bytes only, no protobuf/IP parsing. |
| #041 | IPv4/IPv6 packet read/create helpers on plaintext bytes | gated | Layer above raw plaintext, with packet validation. |
| #042 | wireguard-go/Testcontainers interoperability test | gated | Peer handshake and encrypted exchange harness. |
| #045 | PNet relay to wireguard-go peer interoperability test | gated | Node1 session owner relays through node2 to node3 and back. |
| #046 | Shared-port WireGuard relay registration and demux | gated | Lease registration, MAC1 handshake demux, and receiver-index fast path. |
| #047 | Relay-assisted WireGuard endpoint discovery | gated | Learn relayed endpoint candidate, probe direct, promote only after authenticated response. |
| #048 | PNet-to-PNet WireGuard-compatible transport e2e | gated | Exchange PNet internal `X000PPPP` protobuf frames between two PNet peers. |

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The requested work splits cleanly into handshake, framing, state, cookie, plaintext, IP helper, direct interop, relay interop, shared-port relay demux, relay-assisted endpoint discovery, and PNet-to-PNet e2e slices. | verified | source | The user provided the original seven child scopes, then requested relay topology, shared-port relay registration, relay-assisted endpoint discovery, and PNet-to-PNet e2e as additional children. |
| 2 | F | The raw decrypt boundary should not parse protobuf or require IPv4/IPv6. | verified | source | The user corrected point 7 to raw payload bytes only. |
| 3 | F | The parent should stay gated until the child slices are tracked separately. | verified | source | The user requested a parent tracking issue with gate-depends on every child. |
| 4 | F | Relay interoperability should be tracked as a child of WireGuard-compatible transport mode. | verified | source | The user requested an issue to implement and test `PNet.Mesh` node1 through node2 to `wireguard-go` node3. |
| 5 | F | Shared-port relay registration and demux should be tracked as a child of WireGuard-compatible transport mode. | verified | source | The user requested an issue for shared-port relay registration with MAC1 candidate matching and a fast path. |
| 6 | F | Relay-assisted endpoint discovery should be tracked as a child of WireGuard-compatible transport mode. | verified | source | The user requested an issue for node1 to learn node3's endpoint through node2 and attempt direct communication. |
| 7 | F | PNet-to-PNet transport e2e should be tracked as a child of WireGuard-compatible transport mode. | verified | source | The user requested filing the remaining uncovered proposal for native PNet communication coverage. |
