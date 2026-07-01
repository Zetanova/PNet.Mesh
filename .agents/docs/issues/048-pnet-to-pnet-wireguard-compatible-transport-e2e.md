---
issue: 048
date: 2026-07-01
source: wireguard/tests
priority: high
status: gated
research-status: complete
research-date: 2026-07-01
terminal-state: gated
gate: "Wait for the WireGuard-compatible transport, raw plaintext, and PNet helper slices."
gate-depends:
  - 036
  - 037
  - 038
  - 040
  - 043
  - 044
gate-reason: "Native PNet e2e coverage is blocked until the transport and helper surfaces are implementation-ready."
gate-last-checked: 2026-07-01
gate-status: blocked
assumptions-date: 2026-07-01
brief: "description+playbook"
views:
  enrich: "description+playbook+related-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 048 - PNet To PNet WireGuard-Compatible Transport E2E

## Description
Add an end-to-end test proving that two `PNet.Mesh` peers can establish a WireGuard-compatible transport session and exchange a PNet internal `X000PPPP` protobuf frame over encrypted transport plaintext.

This closes the native PNet communication claim separately from the stock `wireguard-go` interop tests, which must use IPv4 or IPv6 packet plaintext.

## Playbook
- `Topology`: start two PNet.Mesh peers with deterministic keys and transport endpoints.
- `Transport`: establish the WireGuard-compatible handshake and key lifecycle between the PNet peers.
- `PNet frame`: craft a PNet internal frame with the one-byte `X000PPPP` header and protobuf payload.
- `Exchange`: send the frame both directions over encrypted transport packets.
- `Decode`: decrypt, classify as PNet internal payload, validate padding, and deserialize the protobuf payload.
- `Negative cases`: reject invalid marker bits, bad padding count, and non-zero declared padding.

## Research

The existing Testcontainers harness is the right e2e home for native PNet transport, but this issue still depends on the WireGuard-compatible crypto, framing, raw plaintext, and PNet helper slices. Once those land, the test can prove both-direction `X000PPPP` protobuf frame exchange without involving stock `wireguard-go` packet plaintext.

## Related Tracking
- Parent: #035 WireGuard-compatible transport mode.
- Related: #040 raw decrypted WireGuard transport plaintext bytes.
- Related: #043 mesh channel raw payload helpers.
- Related: #044 PNet frame padding and default protobuf header.

## Scope
- In scope: PNet-to-PNet e2e topology, encrypted transport exchange, PNet internal frame crafting/parsing, protobuf round trip, focused invalid-frame checks.
- Out of scope: stock `wireguard-go` interoperability, relay traversal, shared-port relay registration, direct-path promotion.

## Acceptance Criteria
- Two PNet.Mesh peers complete a WireGuard-compatible transport session.
- A valid PNet internal `X000PPPP` protobuf frame can be encrypted, sent, decrypted, classified, and deserialized.
- The same exchange works in both directions.
- The test does not require the plaintext to be IPv4 or IPv6.
- Invalid marker bits, bad padding count, and non-zero declared padding are rejected.
- The test is deterministic and runs in the existing unit/e2e test workflow.

## Assumptions
| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | PNet-to-PNet communication over the WireGuard-compatible transport should support PNet internal protobuf frames, not only IPv4/IPv6 packets. | verified | source | The user separated PNet internal payloads from stock WireGuard peers and designed the `X000PPPP` PNet frame. |
| 2 | F | Existing open issues do not explicitly provide a PNet-to-PNet e2e test for internal protobuf frames. | verified | source | #042 targets `wireguard-go`, while #045-#047 target relay paths to stock WireGuard peers. |
| 3 | F | PNet frame parsing should validate marker and padding behavior. | verified | source | #043 defines the marker and #044 defines padding semantics. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-01 | `gate-depends: [036, 037, 038, 040, 043, 044]` | source | blocked | #036, #037, and #038 are completed, but #040, #043, and #044 remain open, so the issue stays gated. |

## Validation History

- 2026-07-01: dependency gate cleared by #036; remaining dependency gates #037, #038, #040, #043, and #044 keep #048 gated.
- 2026-07-01: dependency gate cleared by #037; remaining dependency gates #038, #040, #043, and #044 keep #048 gated.
- 2026-07-01: dependency gate cleared by #038; remaining dependency gates #040, #043, and #044 keep #048 gated.
