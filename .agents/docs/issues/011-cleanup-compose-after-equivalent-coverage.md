---
issue: 011
date: 2026-06-30
source: e2e/cleanup
priority: medium
status: gated
terminal-state: gated
gate: "Wait until Testcontainers coverage from issues 007, 008, and 009 is complete and README/CI no longer depend on compose."
gate-depends:
  - 007
  - 008
  - 009
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 011 - Cleanup Compose After Equivalent Coverage

## Description

Remove or retire Docker Compose e2e artifacts only after the Testcontainers suite has equivalent or better coverage and all documentation/commands point to the replacement.

## Playbook

- `Coverage gate`: maintainer verifies Testcontainers covers the current compose smoke before compose artifacts are removed.
- `Artifact cleanup`: developer removes obsolete compose files, script paths, and docs references.
- `Compatibility check`: CI/docs no longer require `docker compose` for the mesh smoke path.

## Scope

- Candidate cleanup artifacts include `docker-compose.yml`, `docker-compose.e2e.yml`, `docker-compose.override.yml`, `docker-compose.dcproj`, and `scripts/e2e-mesh-topology.sh`.
- Preserve any compose artifact still needed for Visual Studio, manual debugging, or a documented workflow.
- Update README and project docs after cleanup.

## Acceptance Criteria

- Testcontainers e2e covers the old compose smoke scenario.
- No documented command references removed compose artifacts.
- `rg "docker compose|e2e-mesh-topology|docker-compose" README.md .agents scripts src tests` has only intentional retained references.

## Research

### Current State

The current README still documents the compose smoke command, and the repo contains compose files and a compose script.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The README currently references `scripts/e2e-mesh-topology.sh`. | verified | source | README maintenance commands include the compose e2e script. |
| 2 | F | Compose artifacts are currently the only container e2e implementation in the repo. | verified | source | No Testcontainers project or package reference was found during issue filing. |

## Enrichment History

- 2026-06-30: Kept compose cleanup gated because the README, solution, and runner still depend on compose artifacts. Evidence: `README.md`, `PNet.Mesh.sln`, `scripts/e2e-mesh-topology.sh`, `docker-compose.yml`, `docker-compose.e2e.yml`.

## Completion Report

Pending.
