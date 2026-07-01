---
issue: 039
date: 2026-07-01
source: wireguard/transport
priority: high
status: completed
research-status: complete
research-date: 2026-07-01
terminal-state: completed
split-status: child
parent-issue: 035
assumptions-date: 2026-07-01
completion-date: 2026-07-01
commits: [92a0133193d8af0454b6aa64da93a405e9472991]
brief: "description+playbook"
views:
  enrich: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+playbook+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 039 - Implement WireGuard Cookie Reply And DoS Gate Behavior

## Description

Implement the WireGuard cookie reply and DoS gate behavior needed to protect handshake processing.

## Playbook

- `Cookie secrets`: rotate cookie secrets and bind cookies to the endpoint.
- `Cookie reply`: emit WireGuard cookie reply packets when the gate requires them.
- `Load gate`: require MAC2 under load and rate-limit before expensive handshake or state allocation.

## Research

Primary WireGuard docs define endpoint-bound cookies, a rotating server secret, MAC1/MAC2 load gating, and cookie reply packets. The current code only has manual cookie validation and TODOs, so this is a distinct DoS-gate implementation slice.

## Scope

- In scope: rotating cookie secrets, endpoint-bound cookies, cookie reply packets, MAC2 gate behavior, rate limiting tests.
- Out of scope: packet framing, peer key lifecycle, raw plaintext exposure, IP packet helpers, external interop harness.

## Acceptance Criteria

- Cookie secrets rotate and bind to the observed endpoint.
- The transport path can require MAC2 and emit a cookie reply under load.
- Rate limiting happens before expensive handshake/state allocation.

## Parent Tracking

- Parent: #035
- Extracted scope: cookie reply and DoS gate behavior.
- Standalone reason: this slice can be verified independently of packet framing and peer lifecycle code.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Cookie reply packets and MAC2 gating belong in the transport crypto gate. | verified | source | The user named cookie reply and DoS gate behavior as a separate child. |
| 2 | F | Cookie state must rotate and stay endpoint-bound. | verified | source | The user specified rotating cookie secrets and endpoint-bound cookies. |

## Completion Report

Completed in `92a0133193d8af0454b6aa64da93a405e9472991`.

- Added a WireGuard cookie gate with 16-byte endpoint-bound cookies, two-minute secret rotation, XChaCha20-Poly1305 cookie replies, and MAC2 validation under load.
- Added receive-path gating for WireGuard handshakes so valid MAC1 packets can be rate-limited and cookie-replied before control/session allocation.
- Preserved the legacy PNet receive path by scoping the UDP gate to `PNetMeshTransportMode.WireGuard`.
- Added regression coverage for cookie rotation and endpoint binding, cookie reply round-trip, under-load MAC2 acceptance, and pre-allocation rate limiting.
- Verification passed: initial focused test failed on missing #039 API, focused #039 tests passed, `PNetMeshProtocolTest` passed, Release build passed, scoped whitespace passed, `git diff --check` passed, and the full unit command passed 103/103.

## Resolving Commits

- `92a0133193d8af0454b6aa64da93a405e9472991` - add WireGuard cookie gate
