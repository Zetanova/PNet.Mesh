---
issue: 001
date: 2026-06-30
source: refine-version-check
priority: high
status: completed
type: migration
migration-type: eol
current-version: "net5.0"
target-version: "net10.0"
eol-date: 2022-05-10
research-date: 2026-06-30
research-status: complete
completion-date: 2026-06-30
commits: [30ea5f8]
assumptions-date: 2026-06-30
---

# 001 - .NET 5 to .NET 10 LTS Migration

## Description

The solution targets `net5.0` in all three project files and the test-node Dockerfile uses `mcr.microsoft.com/dotnet/runtime:5.0` plus `sdk:5.0`. The installed SDK reports `NETSDK1138`, so the runtime target is unsupported and no longer receives security updates.

Target .NET 10 LTS directly. Do not use an intermediate runtime target for this migration unless implementation discovers a hard blocker and records it as a follow-up decision. .NET 10 is the installed SDK on this host and the shared user docs track it as the current LTS through 2028-11-14.

Risk: this affects every project, NuGet dependency compatibility, generated protobuf code, Docker base images, and test execution.

## Migration Plan

### Breaking Changes

Research required. Compare .NET 5 to .NET 10 LTS and include package compatibility for `Noise`, `NSec.Cryptography`, `Google.Protobuf`, `Microsoft.Extensions.*`, xUnit, coverlet, and Visual Studio container tooling.

### Dependency Impact

| Dependency | Current | Required | Compatible | Notes |
|-----------|---------|----------|------------|-------|
| Target frameworks | `net5.0` | `net10.0` | unknown | All `.csproj` files need coordinated update. |
| Docker base images | `mcr.microsoft.com/dotnet/*:5.0` | `mcr.microsoft.com/dotnet/*:10.0` | unknown | Update runtime and SDK base images with the framework migration. |
| `Noise` | `1.0.0-pre-201227` | unknown | blocked | Restore currently fails before package audit can run. |

### Estimated Effort

M - multiple project files, Dockerfile, package updates, restore/build/test verification, and likely crypto/protobuf compatibility checks.

### Rollback Strategy

Keep the migration in a dedicated branch or MR. Revert project target/framework and Docker image changes together if restore or protocol tests fail.

### Migration Steps

- [ ] Resolve issue #002 so restore and package audits run.
- [ ] Confirm the package update set required for .NET 10.
- [ ] Update `TargetFramework` values to `net10.0` and Docker base images to .NET 10 together.
- [ ] Refresh packages through `scripts/packages.sh` or explicit `dotnet add package` commands.
- [ ] Run restore, build, unit tests, package audit, and Docker Compose smoke flow.

## Research

### Root Cause

- `src/PNet.Mesh/PNet.Mesh.csproj` targets `net5.0`.
- `tests/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj` targets `net5.0`.
- `tests/PNet.Mesh.TestNode/PNet.Mesh.TestNode.csproj` targets `net5.0`.
- `tests/PNet.Mesh.TestNode/Dockerfile` uses .NET 5 runtime and SDK images.
- `dotnet restore PNet.Mesh.sln` emits `NETSDK1138` for `net5.0`.

### Resolution Approach

Research the .NET 10 package compatibility path first, then migrate project target frameworks, Docker images, and package references in one tested change.

### Code References

- `src/PNet.Mesh/PNet.Mesh.csproj`
- `tests/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj`
- `tests/PNet.Mesh.TestNode/PNet.Mesh.TestNode.csproj`
- `tests/PNet.Mesh.TestNode/Dockerfile`

### Edge Cases & Risks

Crypto and protocol behavior must be regression-tested because this library implements packet framing, Noise protocol handshakes, and packet tracking.

### Complexity Estimate

M - cross-project runtime migration with restore blocker prerequisite.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | All project files currently target `net5.0`. | verified | source | `TargetFramework` entries in the three `.csproj` files are `net5.0`. |
| 2 | F | The test-node Dockerfile uses .NET 5 runtime and SDK base images. | verified | source | `tests/PNet.Mesh.TestNode/Dockerfile` has `mcr.microsoft.com/dotnet/runtime:5.0` and `sdk:5.0`. |
| 3 | F | The installed .NET SDK reports `net5.0` as unsupported. | verified | test | `dotnet restore PNet.Mesh.sln` emitted warning `NETSDK1138`. |
| 4 | F | .NET 5 support ended on 2022-05-10. | verified | external | Microsoft .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core |
| 5 | F | .NET 10 is the current LTS tracked in the user docs library. | verified | source | `~/.agents/docs/tools/dotnet-10.md` records .NET 10 LTS with EOL 2028-11-14. |
| 6 | C | The migration target for this issue is .NET 10 LTS, not an intermediate LTS. | verified | source | User requested updating issue #001 to migrate to .NET 10 on 2026-06-30. |

## Completion Report

Completed in `30ea5f8`.

- Updated all project targets and the test-node Dockerfile to .NET 10.
- Updated NuGet packages, replacing unavailable `Noise` with `Noise.NET` and adapting transport nonce access.
- Migrated unit tests to xUnit v3 in-process execution.
- Added Docker Compose e2e smoke coverage for the six-node topology.

Verification:

- `dotnet restore PNet.Mesh.sln` passed.
- `dotnet build PNet.Mesh.sln -c Release --no-restore` passed with 0 warnings.
- `dotnet run --project tests/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` passed: 24 tests.
- `scripts/e2e-mesh-topology.sh --timeout 180` passed after rebuilding the .NET 10 test-node image.
- NuGet vulnerable/deprecated/outdated checks reported no findings for PackageReference projects; commands still exit 1 because `docker-compose.dcproj` uses packages.config.
