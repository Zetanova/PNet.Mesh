---
issue: 040
date: 2026-07-01
source: wireguard/transport
priority: high
status: gated
research-status: complete
research-date: 2026-07-01
terminal-state: gated
split-status: child
parent-issue: 035
gate: "Wait for the WireGuard crypto/profile, packet framing, and peer lifecycle slices."
gate-depends:
  - 036
  - 037
  - 038
gate-reason: "Raw plaintext exposure depends on the transport crypto, framing, and peer-state foundations; include #039 only if the selected receive path also gates on cookies."
gate-last-checked: 2026-07-01
gate-status: blocked
assumptions-date: 2026-07-01
brief: "description+playbook"
views:
  enrich: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 040 - Expose Decrypted WireGuard Transport Plaintext As Raw Payload Bytes

## Description

Expose decrypted WireGuard transport plaintext as authenticated raw payload bytes plus peer and counter metadata.

## Playbook

- `Raw bytes`: return decrypted transport plaintext as raw authenticated bytes.
- `Metadata`: preserve peer and counter metadata alongside the plaintext bytes.
- `Boundary`: keep protobuf parsing and IPv4/IPv6 parsing out of the decrypt boundary.
- `Padding`: keep WireGuard zero padding compatible and do not use the current PNet padding-length byte here.

## Research

The current source still decrypts into a protobuf packet and strips a trailing PNet padding-count byte before channel delivery. This issue should wait on the WireGuard crypto/profile, packet framing, and peer lifecycle slices so the decrypt boundary can return authenticated raw bytes plus metadata without changing the wire format again.

## Scope

- In scope: transport decrypt API shape, plaintext byte return value, peer/counter metadata, zero-padding handling.
- Out of scope: protobuf parsing, IP packet parsing, packet helper creation, peer lifecycle state, cookie gating, external interop harness.

## Acceptance Criteria

- Decrypt returns authenticated raw plaintext bytes and peer/counter metadata.
- The decrypt boundary does not parse protobuf or require IPv4/IPv6.
- WireGuard zero padding stays compatible with the raw byte boundary.

## Parent Tracking

- Parent: #035
- Extracted scope: raw plaintext bytes and metadata at the decrypt boundary.
- Standalone reason: this slice must stay separate from IP packet parsing and higher-level protocol framing.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Decrypt should return raw payload bytes plus peer/counter metadata. | verified | source | The user corrected point 7 to raw decrypted WireGuard plaintext bytes. |
| 2 | F | The decrypt boundary must not parse protobuf or require IPv4/IPv6. | verified | source | The user explicitly forbade protobuf and IP parsing at this layer. |
| 3 | F | WireGuard zero padding compatibility must not reuse the current PNet padding-length byte. | verified | source | The user required WireGuard-compatible zero padding behavior. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-01 | `gate-depends: [036, 037, 038]` | source | blocked | #036 is completed, but #037 and #038 remain open, so the issue stays gated. |

## Validation History

- 2026-07-01: dependency gate cleared by #036; remaining dependency gates #037 and #038 keep #040 gated.
