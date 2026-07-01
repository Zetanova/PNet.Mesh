---
issue: 011
date: 2026-06-30
source: e2e/cleanup
priority: medium
status: gated
terminal-state: gated
gate: "Wait until README/CI no longer depend on compose."
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

The repo now has Testcontainers e2e coverage, but the current README still documents the compose smoke command and the repo still contains compose files and a compose script.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The README currently references `scripts/e2e-mesh-topology.sh`. | verified | source | README maintenance commands include the compose e2e script. |
| 2 | F | Testcontainers e2e coverage exists while compose artifacts remain present. | verified | source | `src/PNet.Mesh.E2ETests` uses Testcontainers, and README plus `scripts/e2e-mesh-topology.sh` still reference compose. |

## Enrichment History

- 2026-06-30: Removed completed dependency gates #007, #008, and #009 after #009 completed; parent remains gated on README/CI compose cleanup and stale assumptions were revalidated against the new Testcontainers suite.
- 2026-06-30: Kept compose cleanup gated because the README, solution, and runner still depend on compose artifacts. Evidence: `README.md`, `PNet.Mesh.sln`, `scripts/e2e-mesh-topology.sh`, `docker-compose.yml`, `docker-compose.e2e.yml`.

## Completion Report

Pending.
