---
issue: 016
date: 2026-06-30
source: coverage/readme
priority: high
status: open
research-date: 2026-06-30
research-status: partial
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 016 - Cover WireGuard And Noise Security Claims

## Description

Implement and test the security behavior behind the README claim of "same security as wireguard" and the README's Noise Protocol Framework reference.

## Playbook

- `Handshake security`: valid peers complete Noise IKpsk2 handshake and derive working transports.
- `Tamper rejection`: corrupted MAC, wrong PSK, wrong key, replayed packet, and unknown session traffic do not deliver payloads.
- `Cookie/replay guard`: cookie and packet tracker behavior has negative regression tests.
- `Claim hygiene`: README narrows any security wording that is broader than the implemented guarantees.

## Scope

- Add protocol negative tests around `ValidatePacket`, handshake read/write, transport payload read, and replay windows.
- Add integration negative tests for incompatible keys/PSKs.
- Update README if the WireGuard-equivalence wording cannot be justified.

## Acceptance Criteria

- Wrong key/PSK peers cannot exchange accepted payloads.
- Tampered handshake or payload packets are rejected.
- Replay behavior has regression coverage.
- README security wording is accurate for implemented guarantees.

## Research

### Current State

`PNetMeshProtocol` uses Noise IKpsk2 naming in source comments and tests cover happy-path handshakes plus cookie handshakes.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README claims WireGuard-like security and references Noise. | verified | source | README Features and Used Protocols include these claims. |
| 2 | F | Protocol tests currently cover successful handshakes. | verified | source | `PNetMeshProtocolTest` includes handshake exchange tests. |
| 3 | I | The README security claim may need narrowing after formal test coverage is added. | unverified | internal | Requires security-scope review and implementation evidence. |

## Completion Report

Pending.
