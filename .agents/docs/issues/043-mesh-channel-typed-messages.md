---
issue: 043
date: 2026-07-01
source: mesh/channel
priority: high
status: completed
research-status: complete
research-date: 2026-07-01
terminal-state: completed
assumptions-date: 2026-07-01
completion-date: 2026-07-01
commits: [7165d94e69250bb6fce8b4a94f60141bd34ac95f]
brief: "description+playbook"
views:
  enrich: "description+playbook+related-tracking+scope+acceptance-criteria+research+assumptions"
  fix: "description+playbook+related-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 043 - Mesh Channel Raw Payload Helpers

## Description

Keep the mesh channel send/accept and receive paths as raw byte payloads, but add helper and info types so callers can classify, parse, and craft PNet internal payloads, IPv4 packets, and IPv6 packets from those bytes.

This supports the internal PNet plaintext framing discussion around carrying IPv4, IPv6, and PNet protobuf payloads above the WireGuard-compatible raw plaintext boundary without making `PNetMeshChannel` itself a typed-message-only API.

## Playbook

- `Raw boundary`: preserve raw byte channel semantics for send and receive.
- `Payload info`: define helper/info types that describe detected payload kind, version/marker bits, payload length, and remaining payload span.
- `PNet marker`: detect PNet internal frames with first-byte bits `X000....`; `X` may be `0` or `1`.
- `Parse helpers`: detect IPv4, IPv6, and PNet internal frames from raw bytes and return explicit parse success/failure.
- `Craft helpers`: create raw payload bytes from PNet protobuf/raw data or IPv4/IPv6 packet bytes.
- `Compatibility`: keep type-aware parsing optional and adjacent to the channel, not mandatory in the channel core.

## Related Tracking

- Related: #040 exposes decrypted WireGuard transport plaintext as raw payload bytes.
- Related: #041 reads and creates IPv4/IPv6 packets above the raw plaintext layer.

## Scope

- In scope: raw payload helper/info types, PNet marker/type parsing, IPv4/IPv6 detection, raw packet crafting helpers, focused tests.
- In scope: optional convenience APIs only if they preserve the raw byte channel boundary.
- Deferred: use first-byte bit 7 as a varint continuation/extended-header signal later; assume the PNet header is exactly 1 byte for this issue.
- Out of scope: making `PNetMeshChannel` reject raw bytes by default, WireGuard crypto/framing, cookie/rekey behavior, wireguard-go interoperability harness, full IP packet parser implementation.

## Acceptance Criteria

- Existing raw byte send/receive semantics remain available.
- Helpers can classify raw bytes as IPv4, IPv6, PNet internal frame, or invalid/reserved.
- PNet internal frames are identified by first-byte bits `X000....`, where `X` can be `0` or `1`.
- The initial implementation treats the PNet header as 1 byte even when `X = 1`; varint/extended-header parsing is deferred.
- Helpers can craft raw bytes from PNet protobuf/raw data and from IPv4/IPv6 packet bytes.
- Unknown or reserved markers return explicit parse failures.
- Tests cover raw channel preservation, helper classification, packet crafting, and invalid marker handling.

## Research

### Current State

`PNetMeshChannel.TryWrite` accepts raw `ReadOnlyMemory<byte>` and `TryRead` returns raw `ReadOnlyMemory<byte>`. Keep that raw boundary, but remove ad hoc caller-side inference by adding shared helpers and info types.

Report result: keep the channel untyped and add adjacent helper/info types that classify IPv4, IPv6, PNet internal frames, and invalid/reserved payloads without changing the raw boundary.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The current mesh channel public send and receive API is untyped. | verified | source | `PNetMeshChannel.TryWrite` accepts `ReadOnlyMemory<byte>` and `TryRead` returns `ReadOnlyMemory<byte>`. |
| 2 | F | The mesh channel should continue to treat accepted and received data as raw bytes. | verified | source | The user clarified that the channel should still treat raw bytes as the boundary. |
| 3 | F | Internal PNet framing needs helper/info types to distinguish PNet protobuf payloads from IPv4/IPv6 payloads. | verified | source | The user requested helpers and info types for PNet, IPv4, and IPv6 parsing/crafting. |
| 4 | F | IPv4/IPv6 packet parsing is tracked separately from mesh channel raw-helper work. | verified | source | Issue #041 covers IPv4/IPv6 packet read/create helpers. |
| 5 | F | The PNet marker should be first-byte bits `X000....`, with `X` allowed to be `0` or `1`. | verified | source | The user requested the marker form and clarified that `X` is reserved for later varint encoding. |
| 6 | F | The initial PNet header should be treated as exactly 1 byte. | verified | source | The user requested deferring varint behavior and assuming a one-byte header for now. |

## Completion Report

Completed in `7165d94e69250bb6fce8b4a94f60141bd34ac95f`.

- Added `PNetMeshPayloadFraming`, `PNetMeshPayloadFrame`, and explicit parse error/kind enums.
- PNet internal frames now classify first-byte `X000....` headers while treating the header as exactly one byte.
- IPv4/IPv6 helpers classify and craft raw packet payloads through `PNetMeshIpPacket`, trimming authenticated padding beyond IP packet length.
- Kept `PNetMeshChannel` raw byte send semantics unchanged.
- Verification passed: initial focused compile failed on missing #043 helpers, focused #043 tests passed 7/7, Release build passed, scoped whitespace passed, `git diff --check` passed, and the full unit command passed 119/119.

## Resolving Commits

- `7165d94e69250bb6fce8b4a94f60141bd34ac95f` - add mesh payload framing helpers
