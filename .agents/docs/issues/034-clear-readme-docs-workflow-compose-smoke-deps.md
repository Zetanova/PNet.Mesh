---
issue: 034
date: 2026-07-01
source: e2e/docs
priority: medium
status: completed
terminal-state: completed
research-date: 2026-07-01
research-status: complete
assumptions-date: 2026-07-01
completion-date: 2026-07-01
commits: [4cb2ae3019cc4d15a2d4baadf8a53fac08db9844]
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 034 - Clear README Docs And Workflow Compose Smoke Dependencies

## Description

Retire README, project docs, and workflow guidance that still point at the legacy Docker Compose e2e smoke path so issue #011 can later remove compose artifacts without leaving stale user-facing dependencies behind.

This issue does not remove compose artifacts. That cleanup remains tracked by #011.

## Playbook

- `Audit references`: find README, docs, and workflow text that still implies `scripts/e2e-mesh-topology.sh` or `docker compose` is the required mesh smoke path.
- `Redirect guidance`: point users and maintainers at the Testcontainers e2e project and the named scenarios that replace the legacy smoke path.
- `Classify retention`: keep any remaining compose references explicitly labeled as intentional legacy or artifact references until #011 removes the artifacts.

## Scope

- In scope: README maintenance commands, `.agents/docs/e2e-test-stories.md`, `.agents/rules/best-practices.md`, and any current workflow/CI docs if present.
- In scope: wording updates that replace compose-smoke dependency language with Testcontainers language.
- Out of scope: deleting `docker-compose.yml`, `docker-compose.e2e.yml`, `docker-compose.override.yml`, `docker-compose.dcproj`, or `scripts/e2e-mesh-topology.sh`.
- Out of scope: Testcontainers coverage changes or artifact cleanup; those remain under #011.

## Acceptance Criteria

- README and project docs no longer present the compose smoke path as the required mesh e2e command path.
- Any remaining compose references are labeled intentional and legacy.
- The tracker gate in #011 can move past "Wait until README/CI no longer depend on compose."

## Research

### Current State

The current repo still points at the compose smoke path in README maintenance commands and project guidance. A separate CI/workflow tree is not present in the repository scan.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README still references the legacy compose smoke runner. | verified | source | `README.md` includes `timeout 120s scripts/e2e-mesh-topology.sh --no-build --timeout 90`. |
| 2 | F | Project docs still treat the compose smoke path as the multi-node story source. | verified | source | `.agents/docs/e2e-test-stories.md` maps the multi-node mesh topology story to `docker-compose.yml`, `docker-compose.e2e.yml`, and `scripts/e2e-mesh-topology.sh`. |
| 3 | F | Workflow guidance still names the legacy compose smoke path. | verified | source | `.agents/rules/best-practices.md` says legacy smoke uses `scripts/e2e-mesh-topology.sh`. |
| 4 | F | No CI workflow files were present in the repository scan. | verified | source | `rg --hidden --no-ignore --files` found no `.github`, `.gitlab-ci`, or similar workflow config paths. |

## Completion Report

Completed in `4cb2ae3019cc4d15a2d4baadf8a53fac08db9844`.

- Updated the README maintenance commands so the compose smoke runner is explicitly called out as a retained legacy artifact.
- Updated `.agents/docs/e2e-test-stories.md` and `.agents/rules/best-practices.md` to point supported guidance at Testcontainers while keeping the compose path labeled as legacy.
- Synced the root `AGENTS.md` guidance with the same legacy-compose wording.

## Resolving Commits

- `4cb2ae3019cc4d15a2d4baadf8a53fac08db9844` - mark compose smoke path legacy
