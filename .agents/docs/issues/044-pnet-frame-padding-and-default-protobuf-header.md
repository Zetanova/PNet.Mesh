---
issue: 044
date: 2026-07-01
source: mesh/framing
priority: high
status: completed
terminal-state: completed
research-status: complete
research-date: 2026-07-01
assumptions-date: 2026-07-01
completion-date: 2026-07-01
commits: [3a36b4d6599189765c02a0f8d0d858e0baed15f7]
brief: "description+playbook"
views:
  enrich: "description+playbook+related-tracking+scope+acceptance-criteria+research+assumptions"
  fix: "description+playbook+related-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 044 - PNet Frame Padding And Default Protobuf Header

## Description
Define the initial PNet internal frame header as one byte where bits `X000....` mark PNet and the low 4 bits encode padding length `0..15` for a default PNet mesh protobuf payload.

Record the WireGuard compatibility evidence: WireGuard sends zero padding to 16-byte boundaries, while `wireguard-go` trims decrypted IP packets by IPv4/IPv6 length fields rather than validating the trailing padding bytes.

## Playbook
- `Header byte`: parse first byte `X000PPPP`; `X` is reserved for later varint or extended-header use, and `PPPP` is the padding length.
- `Default payload`: a one-byte PNet header means default PNet mesh protobuf payload; `0x0F` represents an empty protobuf payload in one 16-byte plaintext block.
- `Padding trim`: compute `payload_length = plaintext_length - header_length - padding_length`; for this issue, `header_length` is `1`.
- `Padding validation`: for PNet internal frames, validate that declared padding bytes are zero; document this as a PNet canonical-frame rule.
- `WireGuard distinction`: document that WireGuard sends zero padding, but `wireguard-go` receive trims IP packets by IP header length without checking trailing padding bytes.

## Related Tracking
- Related: #040 raw decrypted WireGuard transport plaintext bytes.
- Related: #043 mesh channel raw payload helpers and `X000....` marker.

## Scope
- In scope: PNet one-byte frame header, low-nibble padding count, default protobuf payload slicing, zero-padding validation for PNet frames, focused tests.
- Out of scope: varint or extended-header implementation, WireGuard crypto/framing implementation, IPv4/IPv6 packet parser implementation.

## Acceptance Criteria
- PNet internal frame parser accepts first byte `X000PPPP` and rejects non-PNet markers.
- Low 4 bits decode padding length `0..15`.
- With one-byte header, `0x0F` in a 16-byte plaintext block yields an empty default protobuf payload.
- Parser slices payload as `plaintext[1 .. plaintext_length - padding_length]`.
- Parser validates declared PNet padding bytes are zero and rejects non-zero padding for PNet frames.
- Issue documentation clearly distinguishes PNet strict zero-padding validation from `wireguard-go` receive behavior.

## Research

### WireGuard Padding Behavior
WireGuard pads transport plaintext to a 16-byte multiple with zero bytes before encryption. The `wireguard-go` sender uses `PaddingMultiple = 16`, calculates padding, appends bytes from a zero-filled `[PaddingMultiple]byte`, and encrypts the result.

On receive, `wireguard-go` decrypts the full plaintext, then uses the IPv4 total-length or IPv6 payload-length fields to slice the plaintext to the real IP packet length. The receive path does not inspect trailing padding bytes for zero. Therefore PNet may require zero padding for canonical PNet frames, but that is stricter than observed `wireguard-go` receive behavior.

Sources:
- WireGuard protocol docs: https://www.wireguard.com/protocol/
- `wireguard-go` sender: https://git.zx2c4.com/wireguard-go/tree/device/send.go
- `wireguard-go` receiver: https://git.zx2c4.com/wireguard-go/tree/device/receive.go

## Assumptions
| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | WireGuard transport plaintext is padded to a 16-byte multiple with zero bytes before encryption. | verified | source | Official WireGuard protocol documentation says encapsulated packets are zero padded; `wireguard-go` send path appends from a zero-filled padding buffer before AEAD encryption. |
| 2 | F | `wireguard-go` receive does not validate trailing padding bytes as zero for IP packets. | verified | source | After decrypt, `wireguard-go` slices IPv4/IPv6 packets by their header length fields and does not inspect trailing bytes. |
| 3 | F | PNet can impose stricter zero-padding validation for PNet internal frames. | verified | logical | PNet internal frame parsing is not stock WireGuard IP receive behavior; #043 defines a PNet-only raw-helper layer. |
| 4 | F | The initial PNet frame header should be exactly 1 byte with first-byte bits `X000....`. | verified | source | #043 records the user decision to defer varint behavior and assume a one-byte PNet header. |

## Completion Report

Completed in `3a36b4d6599189765c02a0f8d0d858e0baed15f7`.

- Extended `PNetMeshPayloadFraming` so PNet headers decode low-nibble padding length and trim `Payload` before declared padding bytes.
- Added strict PNet padding validation: declared padding must fit inside the frame and every declared padding byte must be zero.
- Added canonical `CreatePNet` behavior that emits a 16-byte-aligned default frame, including `0x0F` for an empty protobuf payload block.
- Kept IPv4/IPv6 packet trimming separate from PNet strict padding validation.
- Verification passed: initial focused compile failed on missing #044 padding members/errors, canonical creation compile failed before implementation, focused #044 tests passed 12/12, Release build passed, scoped whitespace passed, and `git diff --check` passed. Full unit command hit known #052 relay timeout twice (`bind_three_server_to_localhost_and_relay_exchange`).

## Resolving Commits

- `3a36b4d6599189765c02a0f8d0d858e0baed15f7` - add PNet payload padding semantics
