---
issue: 006
date: 2026-06-30
source: e2e/testcontainers
priority: high
status: gated
terminal-state: gated
gate: "Close only after child issues 007, 008, 009, and 010 are completed or explicitly superseded."
gate-depends:
  - 007
  - 008
  - 009
  - 010
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

- `Harness foundation`: child #007 creates a reusable Testcontainers xUnit project and node topology builder.
- `Topology parity`: child #008 reproduces the current six-node compose smoke topology in Testcontainers.
- `Coverage expansion`: child #009 adds direct, multi-hop, discovery, restart, and negative e2e scenarios.
- `Unit doubles`: child #010 adds deterministic tests for routing/session/relay decisions that should not require Docker.
- `Cleanup gate`: issue #011 removes compose artifacts only after equivalent Testcontainers coverage exists.

## Scope

- Own sequencing, dependencies, and closure criteria for the e2e migration and coverage expansion.
- Keep child issues independently implementable by topic.
- Do not remove compose artifacts under this issue; that is tracked by #011.

## Acceptance Criteria

- Child issues #007, #008, #009, and #010 are completed or replaced by explicit follow-up issues.
- The README test commands point at the new e2e path.
- The remaining test coverage gaps are tracked as focused feature issues.

## Research

### Current State

The repo has a completed compose smoke story and script, but the current assertion surface is log-based and narrow. Unit/integration tests exist in the unit test project, including localhost direct and relay exchange coverage.

### Code References

- `scripts/e2e-mesh-topology.sh`
- `docker-compose.yml`
- `docker-compose.e2e.yml`
- `tests/PNet.Mesh.UnitTests/PNetMeshServerTests.cs`
- `tests/PNet.Mesh.TestNode/NodeService.cs`

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The current e2e runner is Docker Compose based. | verified | source | `scripts/e2e-mesh-topology.sh` constructs and runs a `docker compose` command. |
| 2 | F | Current compose e2e coverage checks a six-node topology. | verified | source | The runner lists `node00`, `node01`, `node10`, `node11`, `node20`, and `node21`. |
| 3 | F | The current unit test project contains localhost server integration tests. | verified | source | `PNetMeshServerTests.cs` includes two-server and three-server exchange tests. |

## Enrichment History

- 2026-06-30: Kept the parent tracker gated while child issues #007, #008, #009, and #010 remain the path to closing the Testcontainers migration. Evidence: `scripts/e2e-mesh-topology.sh`, `README.md`, `PNet.Mesh.sln`.

## Completion Report

Pending.
