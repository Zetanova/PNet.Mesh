---
issue: 013
date: 2026-06-30
source: coverage/readme
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

# 013 - Verify No Extended OS Permissions

## Description

Define, implement, and test the README feature claim that PNet.Mesh requires no extended OS permissions.

## Playbook

- `Permission definition`: maintainer documents what "no extended OS permission" excludes, such as TUN/TAP, raw sockets, privileged containers, or `CAP_NET_ADMIN`.
- `Runtime proof`: tests run mesh communication in an unprivileged process/container.
- `Regression guard`: e2e config avoids privileged container flags and fails if new host-level requirements are introduced.

## Scope

- Clarify the permission claim in docs if needed.
- Add automated checks for unprivileged test-node execution.
- Verify no code path requires raw sockets, device files, privileged capabilities, or admin-only network setup for the covered scenarios.

## Acceptance Criteria

- The permission claim has a precise documented meaning.
- Container e2e runs without privileged mode or added Linux capabilities.
- Any unsupported platform/permission caveat is documented.

## Research

### Open Questions

- Determine whether all target platforms can satisfy the claim with ordinary UDP sockets.
- Decide whether this should be a test-only assertion or a documented support guarantee.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README lists "no extended OS permission required" as a feature. | verified | source | README Features includes that item. |
| 2 | I | Unprivileged Docker containers can model the intended permission boundary for CI. | verified | source | The bind path uses ordinary UDP sockets and the compose files omit privileged flags, raw sockets, and admin capabilities. |

## Enrichment History

- 2026-06-30: Marked ready after confirming the covered scenarios only use ordinary UDP sockets and non-privileged Compose networking. Evidence: `PNetMeshServer.cs`, `docker-compose.yml`, `docker-compose.e2e.yml`.

## Completion Report

Pending.
