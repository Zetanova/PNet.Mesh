---
issue: 005
date: 2026-06-30
source: user/request
priority: medium
status: ready
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 005 - Move Test Projects Under src

## Description

Move the current test projects from `tests/` into the repository's `src/` layout and update every project, solution, Docker, script, and documentation reference that depends on those paths.

## Playbook

- `Project layout migration`: developer moves `PNet.Mesh.UnitTests` and `PNet.Mesh.TestNode`; expected result is a coherent `src/`-centered solution layout.
- `Build/test commands`: operator runs restore, build, unit tests, and e2e smoke; expected result is no stale `tests/` path reference.
- `Container build path`: Dockerfile and future Testcontainers image build use the new test-node project path.

## Scope

- Move `tests/PNet.Mesh.UnitTests` to the agreed `src/` location.
- Move `tests/PNet.Mesh.TestNode` to the agreed `src/` location.
- Update `PNet.Mesh.sln`, project references, Dockerfile paths, compose/Testcontainers paths, scripts, and README commands.
- Keep generated/proto source ownership unchanged unless the move requires path-only updates.

## Acceptance Criteria

- `dotnet restore PNet.Mesh.sln` succeeds.
- `dotnet build PNet.Mesh.sln -c Release --no-restore` succeeds.
- Unit test command succeeds from the new project path.
- No repo reference points to removed `tests/PNet.Mesh.*` project paths except migration history.

## Research

### Code References

- `tests/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj`
- `tests/PNet.Mesh.TestNode/PNet.Mesh.TestNode.csproj`
- `tests/PNet.Mesh.TestNode/Dockerfile`
- `docker-compose.yml`
- `scripts/e2e-mesh-topology.sh`
- `README.md`

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The repo currently contains `tests/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj`. | verified | source | `rg --files` listed the unit test project path. |
| 2 | F | The repo currently contains `tests/PNet.Mesh.TestNode/PNet.Mesh.TestNode.csproj`. | verified | source | `rg --files` listed the test-node project path. |
| 3 | F | The test-node Dockerfile currently references the `tests/PNet.Mesh.TestNode` path. | verified | source | `tests/PNet.Mesh.TestNode/Dockerfile` copies and builds that project path. |

## Enrichment History

- 2026-06-30: Persisted ready state after confirming the migration target still points at the repo's `tests/` projects and related references in the solution, README, Dockerfile, and e2e script. Evidence: `PNet.Mesh.sln`, `README.md`, `tests/PNet.Mesh.TestNode/Dockerfile`, `scripts/e2e-mesh-topology.sh`.

## Completion Report

Pending.
