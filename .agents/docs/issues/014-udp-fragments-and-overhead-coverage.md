---
issue: 014
date: 2026-06-30
source: coverage/readme
priority: high
status: ready
terminal-state: ready
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 014 - Cover UDP Fragments And 32-Byte Encapsulation

## Description

Implement or verify the README claims for communication over UDP data fragments and low 32-byte encrypted payload encapsulation overhead.

## Playbook

- `Fragment transport`: sender splits or batches payload data as designed; receiver reconstructs or delivers the expected payload stream.
- `Encapsulation overhead`: test calculates encrypted payload encapsulation overhead for representative packet types and asserts the documented 32-byte base value.
- `Boundary cases`: tests cover small payloads, payloads near the target size, and oversized payload behavior.

## Scope

- Verify the 32-byte base encrypted payload encapsulation claim for `PacketData`: 16-byte PNet.Mesh transport header plus 16-byte AEAD tag.
- Keep outer UDP/IP headers, padding, and variable protobuf payload framing out of the fixed base-overhead claim.
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
| 1 | F | README lists UDP data fragments and 32-byte encrypted payload encapsulation overhead as features. | verified | source | README Features includes both claims. |
| 2 | F | Protocol packet sizing needs source verification before implementation. | verified | source | `PNetMeshTransport2.WriteMessage` writes a 16-byte `PacketData` header and Noise/ChaChaPoly adds a 16-byte AEAD tag. |

## Clarifications

| Date | Question | Answer | Result |
|------|----------|--------|--------|
| 2026-07-01 | Which layer and packet shape should the README's exact `18 bytes per datagram` overhead claim cover? | Use 32 bytes encrypted payload encapsulation: 16-byte PNet.Mesh transport header plus 16-byte AEAD tag. Ignore outer UDP/IP and padding for the fixed MTU-reduction claim. | README updated; issue ready for tests around packet sizing and fragment/boundary behavior. |

## Enrichment History

- 2026-06-30: Kept clarify because no source or test pins the 18-byte claim to a single packet layer. Evidence: `README.md`, `PNetMeshProtocol.cs`, `PNetMeshSession.cs`.
- 2026-07-01: User clarified the project description should use 32-byte encrypted payload encapsulation, not an 18-byte datagram claim.

## Completion Report

Pending.
