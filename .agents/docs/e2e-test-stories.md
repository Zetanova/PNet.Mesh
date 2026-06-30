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
| Multi-node mesh topology | `docker-compose.yml`, `docker-compose.e2e.yml` | Covered | [003](issues/003-mesh-topology-e2e-story.md) | `scripts/e2e-mesh-topology.sh` starts six nodes, records per-node pong counts, and asserts the node21 -> node20 route. |
| Managed mesh packet exchange | `README.md`, `tests/PNet.Mesh.UnitTests/` | Covered | [003](issues/003-mesh-topology-e2e-story.md) | Unit protocol/server tests cover packet exchange; compose smoke adds an operator-visible route check. |
