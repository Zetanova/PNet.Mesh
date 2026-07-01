---
issue: 018
date: 2026-06-30
source: coverage/readme
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-06-30
terminal-state: completed
commits: [2f083f4]
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

The proto defines candidate exchange and ICE-related fields. `PNetMeshSession` maps relay candidate exchange data, and `PNetMeshTopology` defines candidate types, but comments mark several behaviors as temporary or TODO. README now documents the supported scope as neighbor endpoint detection and relay candidate exchange for covered container flows, and explicitly excludes full ICE checks, STUN, and TURN.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README lists NAT traversal and neighbor detection as a feature and references ICE. | verified | source | README Features and Used Protocols include these items. |
| 2 | F | Candidate exchange types exist in the proto and source model. | verified | source | `MeshProtocol.proto` and `PNetMeshTopology.cs` define candidate exchange structures. |
| 3 | I | Full ICE behavior may not be implemented yet. | verified | source | Source comments and branch behavior show the current ICE path is temporary/lite rather than full ICE. |
| 4 | F | Candidate exchange has deterministic unit coverage. | verified | test | Targeted `PNetMeshRoutingUnitTests` candidate-selection and session-relay candidate-exchange methods passed on 2026-06-30. |
| 5 | F | Segmented Testcontainers routing covers the supported separated-network behavior. | verified | test | Targeted `multi_hop_route_crosses_separated_container_segments` E2E passed on 2026-06-30. |

## Enrichment History

- 2026-06-30: Marked ready after confirming candidate exchange exists in proto and code while full ICE remains a narrower lite path. Evidence: `MeshProtocol.proto`, `PNetMeshTopology.cs`, `PNetMeshServer.cs`.

## Completion Report

Completed on 2026-06-30 in `2f083f4`.

- Narrowed the README feature claim from generic NAT traversal to neighbor endpoint detection and relay candidate exchange for covered container flows.
- Narrowed the README ICE protocol reference to the ICE candidate model and explicitly documented that full ICE checks, STUN, and TURN are not implemented.
- Added README commands for targeted candidate-selection, candidate-exchange, and segmented-route coverage.
- Updated the project story index so candidate exchange and segmented route coverage maps to the README feature claim.

Verification:

- `timeout 120s rtk dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -method PNet.Actor.UnitTests.Mesh.PNetMeshRoutingUnitTests.server_relay_candidate_selection_prefers_known_route_then_reflexive_endpoint`
- `timeout 120s rtk dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -method PNet.Actor.UnitTests.Mesh.PNetMeshRoutingUnitTests.session_relay_roundtrips_route_hop_payload_and_candidate_exchange`
- `timeout 420s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.multi_hop_route_crosses_separated_container_segments`
- `timeout 10s git diff --check`
- `timeout 30s docker ps -a --filter name=pnet-mesh --format '<id> <name> <status>'`
