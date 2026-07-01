---
issue: 006
date: 2026-06-30
source: e2e/testcontainers
priority: high
status: completed
terminal-state: completed
completion-date: 2026-06-30
commits: [52cf1f7, 948f553, 61af492, 56a70ff, 84bb53b, 6a02e9d, 7c09bd6, 49deb32]
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 006 - Testcontainers Migration And Coverage Tracking

## Description

Track the migration from script-driven Docker Compose e2e smoke testing to a Testcontainers-based xUnit e2e suite, plus the unit and integration coverage needed before PNet.Mesh can be treated as more than an exploratory prototype.

This is a parent tracking issue. Implement child issues, not this parent directly.

## Playbook

- `Harness foundation`: child #007 created a reusable Testcontainers xUnit project and node harness.
- `Topology parity`: child #008 reproduced the current six-node compose smoke route in Testcontainers.
- `Coverage expansion`: child #009 adds direct, multi-hop, discovery, restart, and negative e2e scenarios.
- `Unit doubles`: child #010 adds deterministic tests for routing/session/relay decisions that should not require Docker.
- `Cleanup gate`: issue #011 removes compose artifacts only after equivalent Testcontainers coverage exists.

## Scope

- Own sequencing, dependencies, and closure criteria for the e2e migration and coverage expansion.
- Keep child issues independently implementable by topic.
- Do not remove compose artifacts under this issue; that is tracked by #011.

## Acceptance Criteria

- Child issue #009 is completed or replaced by explicit follow-up issues; child issues #007, #008, and #010 are completed.
- The README test commands point at the new e2e path.
- The remaining test coverage gaps are tracked as focused feature issues.

## Research

### Current State

The repo now has a Testcontainers xUnit e2e suite alongside the older compose smoke story and script. README maintenance commands include the Testcontainers suite, while compose artifact removal remains tracked by #011.

### Code References

- `scripts/e2e-mesh-topology.sh`
- `docker-compose.yml`
- `docker-compose.e2e.yml`
- `src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj`
- `src/PNet.Mesh.UnitTests/PNetMeshServerTests.cs`
- `src/PNet.Mesh.TestNode/NodeService.cs`

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | A compose e2e runner still exists. | verified | source | `scripts/e2e-mesh-topology.sh` constructs and runs a `docker compose` command. |
| 2 | F | Current compose e2e coverage checks a six-node topology. | verified | source | The runner lists `node00`, `node01`, `node10`, `node11`, `node20`, and `node21`. |
| 3 | F | The current unit test project contains localhost server integration tests. | verified | source | `PNetMeshServerTests.cs` includes two-server and three-server exchange tests. |
| 4 | F | README maintenance commands include the Testcontainers e2e path. | verified | source | `README.md` runs `src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj`. |

## Enrichment History

- 2026-06-30: Closed the parent tracker after #007, #008, #009, and #010 all completed.
- 2026-06-30: Kept the parent tracker gated while child issues #007, #008, #009, and #010 remain the path to closing the Testcontainers migration. Evidence: `scripts/e2e-mesh-topology.sh`, `README.md`, `PNet.Mesh.sln`.
- 2026-06-30: Removed completed child issue #010 from the active gate after `49deb32`; parent remains gated on #007, #008, and #009.
- 2026-06-30: Removed completed child issue #007 from the active gate after `52cf1f7`; parent remains gated on #008 and #009.
- 2026-06-30: Removed completed child issue #008 from the active gate after `948f553`; parent remains gated on #009.

## Completion Report

Completed on 2026-06-30 after the last dependent coverage issue closed.

- #007 completed the reusable Testcontainers xUnit harness and test-node foundation in `52cf1f7`.
- #008 completed the six-node Testcontainers port of the compose smoke topology in `948f553`.
- #009 completed the broader e2e coverage set in `61af492`, `56a70ff`, `84bb53b`, `6a02e9d`, and `7c09bd6`.
- #010 completed the deterministic routing/session/relay unit coverage in `49deb32`.
- README maintenance commands now include the Testcontainers e2e path.
- With #007, #008, #009, and #010 complete, the parent tracker is closed. Compose artifact cleanup remains gated under #011.

## Resolving Commits

- `52cf1f7` - add Testcontainers xUnit harness
- `948f553` - port compose smoke topology to Testcontainers
- `61af492` - add direct peer Testcontainers coverage
- `56a70ff` - add bootstrap discovery Testcontainers coverage
- `84bb53b` - add multi-hop route Testcontainers coverage
- `6a02e9d` - add restart recovery Testcontainers coverage
- `7c09bd6` - add invalid PSK Testcontainers coverage
- `49deb32` - add deterministic routing unit coverage
