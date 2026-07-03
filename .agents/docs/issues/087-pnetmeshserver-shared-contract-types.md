---
issue: 087
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

# 087 - Extract PNetMeshServer Shared Contract Types

## Description

Split the public/shared contract types from `PNetMeshServer.cs` into focused files.

## Parent Tracking

- Parent: #085
- Extracted scope: move `PNetMeshServerSettings`, `PNetMeshRelayPacket`, and `PNetMeshOutboundMessages` out of the server file.
- Standalone reason: these contracts are shared across channel, session, benchmark, and test code.

## Scope

- In scope: file moves for the shared settings, relay packet, and outbound message contracts.
- Out of scope: server lifecycle, socket processing, relay behavior, and any type renaming.

## Acceptance Criteria

- The shared contract types compile from dedicated files.
- Public and internal names remain unchanged for existing callers.
- Serialization-relevant shape stays stable.
- Build and unit tests pass.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The shared contract types are currently defined in `PNetMeshServer.cs`. | verified | source | `src/PNet.Mesh/PNetMeshServer.cs:19-51` and `src/PNet.Mesh/PNetMeshServer.cs:1181-1290`. |
| 2 | F | These types are referenced outside the server file by channel, session, benchmark, and test code. | verified | source | `rg` output shows references in `PNetMeshChannel`, `PNetMeshSession`, `BenchmarkProtocolHarness`, and unit tests. |

## Completion Report

Resolved in `4cb0090c463832e9b435d141830514303a641ced`.

## Resolving Commits

- `4cb0090c463832e9b435d141830514303a641ced`
