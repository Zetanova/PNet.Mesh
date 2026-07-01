---
issue: 037
date: 2026-07-01
source: wireguard/transport
priority: high
status: ready
research-status: complete
research-date: 2026-07-01
terminal-state: ready
split-status: child
parent-issue: 035
assumptions-date: 2026-07-01
brief: "description+playbook"
views:
  enrich: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 037 - Implement WireGuard Packet Framing And TAI64N Handshake Replay Tracking

## Description

Implement the WireGuard packet framing and handshake replay tracking needed for the transport mode.

## Playbook

- `Message types`: parse 32-bit message type and reserved fields for initiation, response, cookie, and transport packets.
- `Packet sizes`: enforce 148-byte initiation, 92-byte response, 64-byte cookie, and 16-byte transport header layouts.
- `Replay time`: encode and decode TAI64N timestamps and retain per-peer replay tracking for handshake packets.

## Research

WireGuard protocol docs define the 148-byte initiation, 92-byte response, 64-byte cookie, and 16-byte transport header layouts, plus TAI64N replay handling. Local code still uses a 144-byte initiation shape and an 8-byte encrypted timestamp, so this issue is a concrete framing/replay gap.

## Scope

- In scope: packet framing helpers, fixed-size validation, TAI64N encode/decode, replay-tracking tests.
- Out of scope: crypto profile selection, peer lifecycle state, cookie gating, raw plaintext exposure, IP packet helpers, external interop harness.

## Acceptance Criteria

- Packet parsing validates the WireGuard handshake and transport message layouts.
- TAI64N timestamp encode/decode round-trips.
- Replay tracking rejects duplicate or stale handshake packets.

## Parent Tracking

- Parent: #035
- Extracted scope: framing, fixed sizes, and handshake replay timestamps.
- Standalone reason: this slice can be verified without the peer state machine or decrypt boundary changes.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The requested framing work includes 148-byte initiation, 92-byte response, 64-byte cookie, and 16-byte transport header layouts. | verified | source | The user named those packet shapes explicitly. |
| 2 | F | TAI64N timestamp handling belongs with handshake replay tracking. | verified | source | The user requested TAI64N timestamp encode/decode and replay tracking together. |
