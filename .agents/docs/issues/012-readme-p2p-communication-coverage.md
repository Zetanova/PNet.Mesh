---
issue: 012
date: 2026-06-30
source: coverage/readme
priority: high
status: completed
terminal-state: completed
completion-date: 2026-06-30
commits: [96c3efc]
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 012 - Cover README P2P Communication

## Description

Implement and cover the README description that PNet.Mesh is a P2P protocol for managed .NET applications. The coverage should prove direct peer communication at both component and e2e levels.

## Playbook

- `Direct two-peer flow`: two peers open channels and exchange payloads in both directions.
- `Managed .NET flow`: scenario runs through the public .NET API rather than only protocol internals.
- `Container smoke`: equivalent direct peer exchange runs in container e2e once Testcontainers exists.

## Scope

- Keep existing localhost integration tests but make their coverage explicit and reliable.
- Add or link a Testcontainers direct-peer scenario.
- Document the command that verifies the feature.

## Acceptance Criteria

- Direct two-peer payload exchange has a named regression test.
- The test asserts both peers receive the expected payloads.
- README or project testing docs identify the feature coverage command.

## Research

### Current State

`PNetMeshServerTests` has named two-server localhost exchange coverage, and `PNetMeshTestNodeHarnessTests` has matching Testcontainers direct-peer coverage. README and the project story index now map both targeted commands to the P2P feature.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README describes PNet.Mesh as a P2P protocol. | verified | source | README Description says "P2P protocol to use inside managed dotnet application". |
| 2 | F | Existing tests include a two-server localhost exchange. | verified | source | `PNetMeshServerTests` contains `direct_peers_exchange_payloads_in_both_directions_over_public_api`. |
| 3 | F | Existing Testcontainers coverage includes a direct-peer exchange. | verified | source | `PNetMeshTestNodeHarnessTests` contains `direct_peers_exchange_payloads_in_both_directions`. |

## Enrichment History

- 2026-06-30: Completed after `96c3efc` renamed the localhost direct-peer regression and documented targeted unit and Testcontainers commands.
- 2026-06-30: Marked ready after confirming direct two-peer localhost exchange already exists and can anchor the feature coverage command. Evidence: `PNetMeshServerTests.cs` and `README.md`.

## Completion Report

Completed on 2026-06-30 in `96c3efc`.

- Renamed the public-API localhost two-peer exchange test to `direct_peers_exchange_payloads_in_both_directions_over_public_api`.
- Added README commands for the targeted unit regression and the matching Testcontainers direct-peer E2E regression.
- Updated the project story index so the README P2P description maps to the unit and E2E coverage.

Verification:

- `timeout 180s rtk dotnet build PNet.Mesh.sln -c Release --no-restore`
- `timeout 120s rtk dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -method PNet.Actor.UnitTests.Mesh.PNetMeshServerTests.direct_peers_exchange_payloads_in_both_directions_over_public_api`
- `timeout 420s rtk dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.direct_peers_exchange_payloads_in_both_directions`
- `timeout 180s dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh.UnitTests/PNetMeshServerTests.cs --no-restore --verify-no-changes --verbosity minimal`
- `timeout 10s git diff --check`
