---
issue: 003
date: 2026-06-30
source: refine-story-gap
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
terminal-state: ready
completion-date: 2026-06-30
commits: [30ea5f8]
assumptions-date: 2026-06-30
---

# 003 - Mesh Topology E2E Story

## Description

The repo contains a Docker Compose topology with six test nodes across three mesh networks, peer routes, and host bridge endpoints, but there is no `.agents/docs/e2e-test-stories.md` entry or runnable e2e flow documenting the operator-level success criteria.

Add a focused e2e story and a runnable flow after restore is healthy. The flow should prove the compose topology starts, peers can reach the expected mesh route, and logs expose enough signal to diagnose failures.

## Playbook

- `Multi-node compose flow`: operator runs Docker Compose; node00 bridges meshnet0; node10/node11 route through host bridge; node20/node21 form meshnet2; expected result is a healthy multi-hop mesh test topology.
- `Protocol smoke flow`: test validates packet exchange through the route, not just process startup.
- `Gap`: create a runnable e2e flow and connect it to the story index after issue #002 unblocks restore/build.

## Research

### Root Cause

`docker-compose.yml` defines the topology, but no e2e story index or e2e runner exists in the project.

### Resolution Approach

After restore works, add the smallest reproducible e2e flow for the compose topology and map it in `.agents/docs/e2e-test-stories.md`.

### Code References

- `docker-compose.yml`
- `tests/PNet.Mesh.TestNode/NodeService.cs`
- `tests/PNet.Mesh.TestNode/appsettings.json`

### Edge Cases & Risks

The flow may need explicit readiness checks and bounded timeouts so failed UDP peer discovery does not hang the suite.

### Complexity Estimate

M - requires restore/build first, then container startup and network assertions.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `docker-compose.yml` defines six test-node services. | verified | source | Services `node00`, `node01`, `node10`, `node11`, `node20`, and `node21` are present. |
| 2 | F | The project did not have an e2e story index before refine. | verified | source | No `.agents/docs/e2e-test-stories.md` existed. |
| 3 | F | Restore/build is currently blocked before a runnable compose e2e flow can be validated. | verified | test | Restore failed with `NU1102` for `Noise` as tracked in issue #002. |

## Completion Report

Completed in `30ea5f8`.

- Added `docker-compose.e2e.yml` with staged test-node timing and Linux host-gateway support.
- Added `scripts/e2e-mesh-topology.sh`, a bounded runner that builds/starts the six-node topology, waits for per-node final counts, and asserts node21 reaches node20.
- Updated `.agents/docs/e2e-test-stories.md` and `README.md` with the runnable flow.

Verification:

- `docker compose -f docker-compose.yml -f docker-compose.e2e.yml config --quiet` passed.
- `scripts/e2e-mesh-topology.sh --timeout 180` passed and rebuilt the .NET 10 test-node image with 0 compiler warnings.
