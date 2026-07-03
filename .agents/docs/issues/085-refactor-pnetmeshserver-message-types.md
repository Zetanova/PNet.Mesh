---
issue: 085
date: 2026-07-03
source: refactor-audit
priority: low
status: completed
split-status: parent
gate-depends: [087, 088]
gate-reason: "Tracking parent waits for fine-grained child issues"
ungate-when: "All child issues are completed"
terminal-state: completed
research-status: complete
research-date: 2026-07-03
assumptions-date: 2026-07-03
gate-last-checked: 2026-07-03
gate-status: cleared
completed-date: 2026-07-03
completed-commits:
  - 4cb0090c463832e9b435d141830514303a641ced
brief: "description+scope+acceptance-criteria+assumptions"
views:
  fix: "description+scope+acceptance-criteria+proposed-refactor+verification+assumptions"
  enrich: "description+scope+acceptance-criteria+proposed-refactor+verification+assumptions"
  complete: "description+completion-report+resolving-commits"
---

# 085 - Refactor PNetMeshServer Message Types

## Description

Source proposal: `RP-003`.

`PNetMeshServer.cs` is 1,291 lines and contains both the server implementation and shared DTO/command/work-item types. Several of those types are referenced by channel, session, benchmark, and unit-test code, so the file is doing more than one cohesive job.

This issue now tracks the split child issues below.

## Scope

- In scope: file organization only for `PNetMeshServerSettings`, `PNetMeshRelayPacket`, `PNetMeshControlCommands`, `PNetMeshOutboundMessages`, and socket work-item types.
- Preserve namespace, accessibility, public API behavior, and serialization-relevant contracts.
- Out of scope: server lifecycle behavior, routing decisions, socket I/O changes, and protocol changes.

## Acceptance Criteria

- Shared DTO and command families live in focused files instead of the server implementation file.
- `PNetMeshServer.cs` remains centered on server lifecycle, socket receive/send, routing, and relay decisions.
- Public and internal type names remain stable for existing callers and tests.
- Build and unit tests pass after the file split.

## Tracking

| Child | Scope | Status | Notes |
|-------|-------|--------|-------|
| #087 | Shared server contract types | open | Standalone shared-contract extraction |
| #088 | Server control commands and socket work-item types | open | Standalone internal-type extraction |

## Residual Scope
none

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-03 | `gate-depends: [087, 088]` | source | completed | #087 and #088 are completed, so the parent tracking issue is complete. |

## Validation History

- 2026-07-03: dependency gate cleared by #087; remaining dependency gate #088 keeps #085 gated.
- 2026-07-03: dependency gate cleared by #088; all child issues are complete, so #085 is completed.

## Proposed Refactor

Move shared type families into files such as `PNetMeshServerSettings.cs`, `PNetMeshRelayPacket.cs`, `PNetMeshControlCommands.cs`, `PNetMeshOutboundMessages.cs`, and a private/internal socket work-item file. Avoid behavioral changes.

## Verification

```bash
dotnet build PNet.Mesh.sln -c Release --no-restore
dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none
dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh/PNetMeshServer.cs --no-restore --verify-no-changes --verbosity minimal
```

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `PNetMeshServer.cs` is 1,291 lines. | verified | test | `wc -l src/PNet.Mesh/PNetMeshServer.cs` returned 1,291. |
| 2 | F | `PNetMeshServer.cs` contains server settings, the server implementation, socket work items, relay packet, control command, and outbound message types. | verified | source | `src/PNet.Mesh/PNetMeshServer.cs:19-51` and `src/PNet.Mesh/PNetMeshServer.cs:1163-1290`. |
| 3 | F | These message and command types are referenced outside `PNetMeshServer.cs`. | verified | source | `PNetMeshChannel`, `PNetMeshSession`, and benchmark harness code reference `PNetMeshRelayPacket`, `PNetMeshControlCommands`, and `PNetMeshOutboundMessages`. |

## Completion Report

Completed the tracking parent after child issues #087 and #088 landed in `4cb0090c463832e9b435d141830514303a641ced`.

## Resolving Commits

- `4cb0090c463832e9b435d141830514303a641ced`
