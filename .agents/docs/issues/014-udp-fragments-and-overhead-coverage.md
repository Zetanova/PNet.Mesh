---
issue: 014
date: 2026-06-30
source: coverage/readme
priority: high
status: open
research-date: 2026-06-30
research-status: none
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 014 - Cover UDP Fragments And Datagram Overhead

## Description

Implement or verify the README claims for communication over UDP data fragments and low overhead of 18 bytes per datagram.

## Playbook

- `Fragment transport`: sender splits or batches payload data as designed; receiver reconstructs or delivers the expected payload stream.
- `Datagram overhead`: test calculates the protocol overhead for representative packet types and asserts the documented value or updates the claim.
- `Boundary cases`: tests cover small payloads, payloads near the target size, and oversized payload behavior.

## Scope

- Identify the exact packet type and layer where the 18-byte overhead claim applies.
- Add protocol/component tests for packet sizing.
- Add integration coverage for UDP payload exchange around boundary sizes.
- Update README if the claim is inaccurate or only applies to a subset.

## Acceptance Criteria

- Tests prove the documented overhead or the README claim is corrected.
- Fragment/boundary behavior has deterministic tests.
- Container e2e includes at least one non-trivial payload size.

## Research

### Code References

- `src/PNet.Mesh/PNetMeshSession.cs`
- `src/PNet.Mesh/Protos/MeshProtocol.proto`
- `src/PNet.Mesh/PNetMeshProtocol.cs`

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README lists UDP data fragments and 18-byte datagram overhead as features. | verified | source | README Features includes both claims. |
| 2 | F | Protocol packet sizing needs source verification before implementation. | unverified | source | The README claim is not currently tied to a named automated test. |

## Completion Report

Pending.
