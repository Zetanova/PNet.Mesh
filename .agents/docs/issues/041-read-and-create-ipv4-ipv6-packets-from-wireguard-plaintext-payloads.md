---
issue: 041
date: 2026-07-01
source: wireguard/transport
priority: medium
status: ready
research-status: complete
research-date: 2026-07-01
terminal-state: ready
split-status: child
parent-issue: 035
gate: "Wait for the raw plaintext boundary."
gate-depends:
  - 040
gate-reason: "Packet helpers consume the decrypted raw byte boundary; stock WireGuard interop also requires the transport crypto/profile slices."
gate-last-checked: 2026-07-01
gate-status: cleared
assumptions-date: 2026-07-01
brief: "description+playbook"
views:
  enrich: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 041 - Read And Create IPv4/IPv6 Packets From WireGuard Plaintext Payloads

## Description

Read and create IPv4 and IPv6 packets on top of decrypted WireGuard plaintext payload bytes.

## Playbook

- `Packet helpers`: add IPv4/IPv6 read and create helpers above the raw decrypted payload boundary.
- `Metadata`: preserve source and destination metadata with packet read/create operations.
- `Validation`: check length and family-specific packet validity before packet creation or parsing.
- `Future routing`: keep AllowedIPs integration as a later transport-policy concern.

## Research

These helpers sit above the raw plaintext boundary, so they should consume or produce authenticated decrypted bytes rather than change decrypt behavior. Current source only maps endpoints, not IPv4/IPv6 packet structures, and stock WireGuard interop still needs the underlying transport profile and raw plaintext work first.

## Scope

- In scope: IPv4/IPv6 packet parsing and creation helpers, source/destination metadata, packet length validation, tests.
- Out of scope: transport decrypt, crypto profile selection, peer lifecycle state, cookie gating, raw plaintext boundary changes, external interop harness.

## Acceptance Criteria

- Valid IPv4 and IPv6 plaintext payloads can be read and created as packets.
- Invalid lengths and unsupported families are rejected.
- The helper layer stays separate from the raw decrypt child.

## Parent Tracking

- Parent: #035
- Extracted scope: IP packet read/create helpers above raw decrypted payload bytes.
- Standalone reason: this slice sits above the decrypt boundary and should not change transport crypto behavior.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | IPv4/IPv6 packet helpers belong above the raw decrypted payload layer. | verified | source | The user requested packet read/create on top of raw decrypted payload bytes. |
| 2 | F | AllowedIPs integration should remain separate from raw transport decrypt. | verified | source | The user asked to keep future AllowedIPs integration separate from the decrypt boundary. |

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-01 | `gate-depends: [040]` | source | ready | #040 is completed, so #041 is implementation-ready. |

## Validation History

- 2026-07-01: dependency gate cleared by #040; #041 is now ready.
