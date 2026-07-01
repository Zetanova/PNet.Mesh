---
issue: 013
date: 2026-06-30
source: coverage/readme
priority: medium
status: completed
terminal-state: completed
completion-date: 2026-06-30
commits: [5c4d336]
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
| 2 | I | Unprivileged Docker containers can model the intended permission boundary for CI. | verified | source | Testcontainers test-node containers now force non-privileged mode and drop `NET_ADMIN` and `NET_RAW`. |
| 3 | F | Covered container flows run without effective `CAP_NET_ADMIN`, effective `CAP_NET_RAW`, or a TUN/TAP device. | verified | test | `test_node_runs_without_extended_network_permissions` failed before the harness fix due `CAP_NET_RAW` and passed after `5c4d336`. |

## Enrichment History

- 2026-06-30: Completed after `5c4d336` documented the container-flow permission boundary, dropped network-admin/raw capabilities in Testcontainers, and added a runtime E2E guard.
- 2026-06-30: Marked ready after confirming the covered scenarios only use ordinary UDP sockets and non-privileged Compose networking. Evidence: `PNetMeshServer.cs`, `docker-compose.yml`, `docker-compose.e2e.yml`.

## Completion Report

Completed on 2026-06-30 in `5c4d336`.

- README now defines the no-extended-permission claim for covered container flows: ordinary UDP sockets, no TUN/TAP device, no raw-socket capability, no `CAP_NET_ADMIN`, and no privileged container mode.
- The Testcontainers harness forces `Privileged = false` and drops `NET_ADMIN` and `NET_RAW` for every test-node container.
- Added `test_node_runs_without_extended_network_permissions`, which starts a real test-node container, checks `/dev/net/tun`, decodes `CapEff`, and asserts `CAP_NET_ADMIN` and `CAP_NET_RAW` are not effective.

Verification:

- Targeted permission E2E failed before the harness fix because `CAP_NET_RAW` was effective.
- `timeout 180s rtk dotnet build PNet.Mesh.sln -c Release --no-restore`
- `timeout 420s rtk dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.test_node_runs_without_extended_network_permissions`
- `timeout 900s rtk dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none`
- `timeout 180s dotnet format whitespace PNet.Mesh.sln --include src/PNet.Mesh.E2ETests/PNetMeshTestNodeHarness.cs src/PNet.Mesh.E2ETests/PNetMeshTestNodeHarnessTests.cs --no-restore --verify-no-changes --verbosity minimal`
- `timeout 10s git diff --check`
