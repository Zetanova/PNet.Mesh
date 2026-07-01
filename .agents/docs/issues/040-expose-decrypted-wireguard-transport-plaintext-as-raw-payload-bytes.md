---
issue: 040
date: 2026-07-01
source: wireguard/transport
priority: high
status: completed
research-status: complete
research-date: 2026-07-01
terminal-state: completed
split-status: child
parent-issue: 035
gate: "Wait for the WireGuard crypto/profile, packet framing, and peer lifecycle slices."
gate-depends:
  - 036
  - 037
  - 038
gate-reason: "Raw plaintext exposure depends on the transport crypto, framing, and peer-state foundations; include #039 only if the selected receive path also gates on cookies."
gate-last-checked: 2026-07-01
gate-status: cleared
assumptions-date: 2026-07-01
completion-date: 2026-07-01
commits: [5cf316b9925023e05bbf7ec9cc648e70add70667]
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
| 2026-07-01 | `gate-depends: [036, 037, 038]` | source | ready | #036, #037, and #038 are completed, so #040 is implementation-ready. |

## Validation History

- 2026-07-01: dependency gate cleared by #036; remaining dependency gates #037 and #038 keep #040 gated.
- 2026-07-01: dependency gate cleared by #037; remaining dependency gate #038 keeps #040 gated.
- 2026-07-01: dependency gate cleared by #038; #040 is now ready.

## Completion Report

Completed in `5cf316b9925023e05bbf7ec9cc648e70add70667`.

- Added raw transport plaintext metadata exposing decrypted byte count, counter, keypair, and peer metadata.
- Added `TryReadPlaintext` so WireGuard callers can consume authenticated raw bytes without protobuf or IP parsing.
- Switched WireGuard packet data writes to zero padding without the legacy PNet padding-length byte while preserving PNet behavior.
- Added regression coverage for zero-padded raw plaintext, metadata, and exact packet sizing for 16-byte WireGuard payloads.
- Verification passed: initial focused test failed on missing #040 API, focused #040 tests passed, `PNetMeshProtocolTest` passed, Release build passed, scoped whitespace passed, and `git diff --check` passed. Full unit verification still intermittently hits known issue #052.

## Resolving Commits

- `5cf316b9925023e05bbf7ec9cc648e70add70667` - expose WireGuard raw plaintext
