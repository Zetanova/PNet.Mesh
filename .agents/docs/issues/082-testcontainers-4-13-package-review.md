---
issue: 082
date: 2026-07-03
source: refine-version-check
priority: medium
status: open
type: migration
migration-type: version
research-date: 2026-07-03
research-status: complete
assumptions-date: 2026-07-03
---

# 082 - Testcontainers 4.13 Package Review

## Description

The refine package dry-run found a direct dependency update for the e2e test project: `Testcontainers` `4.12.0 -> 4.13.0`.

## Scope

- Review the `Testcontainers` 4.13.0 release notes and compatibility with the current Docker Desktop/Testcontainers e2e setup.
- Update `src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj` only if the release is compatible.
- Rerun the affected bounded Testcontainers e2e batches after the update.

## Acceptance Criteria

- `scripts/packages.sh --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj --name Testcontainers --dry-run` reports no newer stable package, or the update is explicitly deferred with a reason.
- `dotnet restore PNet.Mesh.sln` passes.
- `dotnet build PNet.Mesh.sln -c Release --no-restore` passes.
- README-documented Testcontainers e2e batches pass on the updated package.

## Research

- `timeout 600s scripts/packages.sh --dry-run --timeout 60` reported `would src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj Testcontainers 4.12.0 -> 4.13.0`.
- The current Testcontainers e2e batches passed on `Testcontainers` 4.12.0 during the 2026-07-03 refine run.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj` directly references `Testcontainers` 4.12.0. | verified | source | The project file contains `<PackageReference Include="Testcontainers" Version="4.12.0" />`. |
| 2 | F | The package helper currently resolves stable `Testcontainers` 4.13.0 as the available update. | verified | test | `scripts/packages.sh --dry-run --timeout 60` reported the update on 2026-07-03. |
| 3 | F | The current e2e suite passes on `Testcontainers` 4.12.0. | verified | test | All five README-documented bounded e2e batches passed on 2026-07-03 before the update. |

## Completion Report

Pending.
