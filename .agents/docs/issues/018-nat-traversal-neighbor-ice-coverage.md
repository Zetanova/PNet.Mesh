---
issue: 018
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

# 018 - Cover NAT Traversal Neighbor Detection And ICE

## Description

Implement and test the README feature claim for NAT traversal and neighbor detection, plus the README ICE protocol reference. If the implementation only supports a narrower candidate-exchange behavior, update the README to match.

## Playbook

- `Candidate exchange`: peers exchange host/server-reflexive candidate data and choose a reachable endpoint.
- `Neighbor detection`: peers learn a usable endpoint from observed traffic or relay-provided candidates.
- `NAT-like topology`: e2e scenario models separated networks and host-published UDP behavior.
- `Claim hygiene`: README distinguishes implemented candidate exchange from full ICE if full ICE is not implemented.

## Scope

- Verify the intended NAT traversal support level.
- Add tests around candidate serialization/mapping and endpoint selection.
- Add container e2e coverage for separated network segments.
- Update README if full ICE or NAT traversal is aspirational.

## Acceptance Criteria

- Candidate exchange has deterministic tests.
- A NAT-like or network-partitioned e2e scenario proves the supported behavior.
- Unsupported ICE/NAT features are documented or tracked separately.

## Research

### Current State

The proto defines candidate exchange and ICE-related fields. `PNetMeshSession` maps relay candidate exchange data, and `PNetMeshTopology` defines candidate types, but comments mark several behaviors as temporary or TODO.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README lists NAT traversal and neighbor detection as a feature and references ICE. | verified | source | README Features and Used Protocols include these items. |
| 2 | F | Candidate exchange types exist in the proto and source model. | verified | source | `MeshProtocol.proto` and `PNetMeshTopology.cs` define candidate exchange structures. |
| 3 | I | Full ICE behavior may not be implemented yet. | unverified | internal | Source comments include TODOs; enrichment must classify exact support. |

## Completion Report

Pending.
