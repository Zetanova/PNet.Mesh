---
issue: 016
date: 2026-06-30
source: coverage/readme
priority: high
status: completed
split-status: parent
terminal-state: completed
gate: "Close only after child issues 028, 029, 030, and 031 are completed or explicitly superseded."
gate-depends:
  - 028
  - 029
  - 030
  - 031
gate-reason: "Tracking parent waits for fine-grained child issues"
ungate-when: "All child issues are completed"
gate-last-checked: 2026-07-01
gate-status: cleared
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-07-01
commits: [b36bd7fd8d7f82308624bbaf428daf660aaf096b, 7c66b9589b6e3e8c660b2fd55ed063823f3312a0, 032008382cb8bbf8e8efee67b33542fb83981e85, 172e333b80860435c09b265dbdbab7805783d4fe]
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
| #028 | Handshake and authentication happy path | completed | Valid peers complete Noise IKpsk2 handshake |
| #029 | Tamper rejection | completed | Wrong key/PSK, corrupted packet, unknown receiver, and tampered counter rejection |
| #030 | Replay and cookie guard | completed | Replay windows, cookie MACs, and packet tracker guards |
| #031 | README security claim hygiene | completed | Narrow claim wording to implemented guarantees |

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

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-01 | `gate-depends: [028, 029, 030, 031]` | source | blocked | #028 is completed, but #029, #030, and #031 remain open, so the parent stays gated. |
| 2026-07-01 | `gate-depends: [028, 029, 030, 031]` | source | blocked | #028 and #029 are completed, but #030 and #031 remain open, so the parent stays gated. |
| 2026-07-01 | `gate-depends: [028, 029, 030, 031]` | source | blocked | #028, #029, and #030 are completed, but #031 remains open, so the parent stays gated. |
| 2026-07-01 | `gate-depends: [028, 029, 030, 031]` | source | passed | #028, #029, #030, and #031 are completed, so the parent gate is cleared. |

## Validation History

- 2026-07-01: dependency gate cleared by #028; remaining dependency gates #029, #030, and #031 keep #016 gated.
- 2026-07-01: dependency gate cleared by #029; remaining dependency gates #030 and #031 keep #016 gated.
- 2026-07-01: dependency gate cleared by #030; remaining dependency gate #031 keeps #016 gated.
- 2026-07-01: dependency gate cleared by #031; all child issues are completed, so #016 is completed.

## Completion Report

Completed through child issues #028, #029, #030, and #031.

- #028 added named happy-path coverage for Noise IKpsk2 handshakes, derived transports, and bidirectional payload exchange.
- #029 added tamper rejection coverage for wrong keys/PSKs, corrupted handshake MACs, tampered payloads, unknown receiver indexes, and tampered-packet replay-state poisoning.
- #030 added replay, cookie MAC, and packet tracker guard coverage.
- #031 narrowed README security wording to the implemented/tested guarantees and removed full WireGuard-equivalence language.

## Resolving Commits

- `b36bd7fd8d7f82308624bbaf428daf660aaf096b` - handshake and authentication happy-path coverage
- `7c66b9589b6e3e8c660b2fd55ed063823f3312a0` - tamper rejection coverage and authentication-failure handling
- `032008382cb8bbf8e8efee67b33542fb83981e85` - replay, cookie, and packet-tracker guard coverage
- `172e333b80860435c09b265dbdbab7805783d4fe` - README security claim hygiene
