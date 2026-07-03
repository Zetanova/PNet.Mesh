---
issue: 088
date: 2026-07-03
source: refactor-audit
priority: low
status: completed
terminal-state: completed
research-status: complete
research-date: 2026-07-03
assumptions-date: 2026-07-03
completed-date: 2026-07-03
completed-commits:
  - 4cb0090c463832e9b435d141830514303a641ced
split-status: child
parent-issue: 085
brief: "description+scope+acceptance-criteria+assumptions"
views:
  fix: "description+parent-tracking+scope+acceptance-criteria+assumptions"
  enrich: "description+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report+resolving-commits"
---

# 088 - Extract PNetMeshServer Control Commands and Work Items

## Description

Split the server-internal control command and socket work-item types out of `PNetMeshServer.cs`.

## Parent Tracking

- Parent: #085
- Extracted scope: move `PNetMeshControlCommands`, `PNetMeshSocketReceiveWorkItem`, and `PNetMeshSocketSendWorkItem` into focused files.
- Standalone reason: these are internal coordination types and do not belong in the server implementation body.

## Scope

- In scope: file moves for the control command hierarchy and socket work-item helpers.
- Out of scope: server lifecycle, shared contract types, and any message-shape changes.

## Acceptance Criteria

- The control command hierarchy compiles from its own file.
- The socket work-item types compile from their own file.
- Existing server and test callers continue to compile without type-name changes.
- Build and unit tests pass.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The control commands and socket work-item types are currently defined near the end of `PNetMeshServer.cs`. | verified | source | `src/PNet.Mesh/PNetMeshServer.cs:1169-1290`. |
| 2 | F | The socket work-item types are server-internal helpers, not cross-cutting API surface. | verified | source | `rg` output only shows the work-item types in `src/PNet.Mesh/PNetMeshServer.cs`. |
| 3 | F | `PNetMeshControlCommands` is used by server, channel, session, benchmark, and test code. | verified | source | `rg` output shows references in `PNetMeshServer`, `PNetMeshSession`, `PNetMeshChannel`, and unit tests. |

## Completion Report

Resolved in `4cb0090c463832e9b435d141830514303a641ced`.

## Resolving Commits

- `4cb0090c463832e9b435d141830514303a641ced`
