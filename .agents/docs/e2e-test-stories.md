---
generated-by: refine
generated-date: 2026-06-30
last-refined: 2026-06-30
project-types: [dotnet, docker/podman, protobuf-grpc]
---

# E2E Test Stories

Project story index for feature-carrying artifacts and operator workflows.

## Stories

| Story | Source | Status | Issue | Notes |
|-------|--------|--------|-------|-------|
| Multi-node mesh topology | `src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj` | Covered | [003](issues/003-mesh-topology-e2e-story.md) | Historical six-node topology is now covered by Testcontainers; supported mesh coverage lives in named methods such as `PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.direct_peers_exchange_payloads_in_both_directions` and `PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.multi_hop_route_crosses_separated_container_segments`. |
| Direct P2P packet exchange | `README.md`, `src/PNet.Mesh.UnitTests/PNetMeshServerTests.cs`, `src/PNet.Mesh.E2ETests/PNetMeshTestNodeHarnessTests.cs` | Covered | [012](issues/012-readme-p2p-communication-coverage.md) | Targeted unit and Testcontainers methods assert direct payload delivery in both directions. |
| Candidate exchange and segmented route | `README.md`, `src/PNet.Mesh.UnitTests/PNetMeshRoutingUnitTests.cs`, `src/PNet.Mesh.E2ETests/PNetMeshTestNodeHarnessTests.cs` | Covered | [018](issues/018-nat-traversal-neighbor-ice-coverage.md) | Targeted unit methods cover relay candidate selection and candidate exchange; segmented Testcontainers routing covers the supported separated-network behavior. |
