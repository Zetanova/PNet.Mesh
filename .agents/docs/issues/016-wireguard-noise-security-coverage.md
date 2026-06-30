---
issue: 016
date: 2026-06-30
source: coverage/readme
priority: high
status: gated
split-status: parent
terminal-state: gated
gate: "Close only after child issues 028, 029, 030, and 031 are completed or explicitly superseded."
gate-depends:
  - 028
  - 029
  - 030
  - 031
gate-reason: "Tracking parent waits for fine-grained child issues"
ungate-when: "All child issues are completed"
research-date: 2026-06-30
research-status: complete
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

This is a parent tracking issue. Implement child issues, not this parent directly.

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

## Tracking

| Child | Scope | Status | Notes |
|-------|-------|--------|-------|
| #028 | Handshake and authentication happy path | open | Valid peers complete Noise IKpsk2 handshake |
| #029 | Tamper rejection | open | Wrong key/PSK and corrupted packet rejection |
| #030 | Replay and cookie guard | open | Replay windows and cookie handshakes |
| #031 | README security claim hygiene | open | Narrow claim wording to implemented guarantees |

## Residual Scope
none

## Research

### Current State

`PNetMeshProtocol` uses Noise IKpsk2 naming in source comments and tests cover happy-path handshakes plus cookie handshakes.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README claims WireGuard-like security and references Noise. | verified | source | README Features and Used Protocols include these claims. |
| 2 | F | Protocol tests currently cover successful handshakes. | verified | source | `PNetMeshProtocolTest` includes handshake exchange tests. |
| 3 | I | The README security claim may need narrowing after formal test coverage is added. | verified | source | Existing coverage proves happy-path handshakes and replay checks, but not the broader negative matrix implied by the README. |

## Enrichment History

- 2026-06-30: Marked ready after confirming the current coverage is happy-path handshake plus replay checks, not the broader negative matrix implied by the README. Evidence: `PNetMeshProtocolTest.cs`, `PNetMeshServerTests.cs`, `PNetMeshProtocol.cs`.

## Completion Report

Pending.
