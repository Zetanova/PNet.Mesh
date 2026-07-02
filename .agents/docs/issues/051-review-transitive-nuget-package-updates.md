---
issue: 051
date: 2026-07-01
source: nuget/audit
priority: medium
status: completed
terminal-state: completed
research-status: complete
assumptions-date: 2026-07-02
completed-date: 2026-07-02
brief: "description+playbook+gate+assumptions"
views:
  enrich: "description+playbook+gate+assumptions"
  fix: "description+playbook+gate+assumptions"
  complete: "description+completion-report"
---

# 051 - Review Transitive NuGet Package Updates

## Description

`dotnet package list --project PNet.Mesh.sln --outdated --include-transitive` reports transitive package updates across the solution. Review them in a dedicated dependency-update pass rather than broadening the WireGuard crypto-profile issue.

## Playbook

- `Audit`: rerun restore plus vulnerable, deprecated, and outdated package checks.
- `Scope`: identify which direct packages bring the outdated transitive dependencies.
- `Update path`: update direct dependencies through the project package-maintenance flow when the transitive updates are desirable.
- `Verification`: run restore, build, unit tests, targeted e2e if affected, and package health checks after any package changes.

## Gate

Resolved in the 2026-07-02 dependency-update pass. No direct package updates are available from the configured NuGet sources, and the stale entries remain transitive-only.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The solution restore succeeded before package health checks ran. | verified | test | `dotnet restore PNet.Mesh.sln --verbosity minimal` completed successfully on 2026-07-02. |
| 2 | F | The dependency audit found no vulnerable packages in the solution. | verified | test | `dotnet package list --project PNet.Mesh.sln --vulnerable --include-transitive` reported no vulnerable packages for all eight projects. |
| 3 | F | The dependency audit found no deprecated packages in the solution. | verified | test | `dotnet package list --project PNet.Mesh.sln --deprecated --include-transitive` reported no deprecated packages for all eight projects. |
| 4 | F | The dependency audit reported transitive package updates across the solution. | verified | test | `dotnet package list --project PNet.Mesh.sln --outdated --include-transitive` still reports transitive updates including `NETStandard.Library`, `Microsoft.NETCore.Platforms`, `Microsoft.Testing.Platform`, `Docker.DotNet.Enhanced`, and `Newtonsoft.Json`. |
| 5 | F | No direct package updates are available from the configured NuGet sources. | verified | test | `dotnet package list --project PNet.Mesh.sln --outdated` reported no updates for all eight projects. |
| 6 | F | The repeated stale transitive packages are introduced by current direct packages rather than missing direct bumps. | verified | source | Restored `project.assets.json` maps examples to current direct packages: `NETStandard.Library <- Noise.NET/1.0.0`, `Docker.DotNet.Enhanced <- Testcontainers/4.12.0`, `Microsoft.Testing.Platform <- xunit.v3.core.mtp-v1/3.2.2`, and `Perfolizer <- BenchmarkDotNet/0.15.8`. |

## Completion Report

The dedicated dependency-update review found no actionable direct package bump. `scripts/packages.sh` remains the package-maintenance path, but `dotnet package list --project PNet.Mesh.sln --outdated` reports no top-level updates from the configured sources.

Verification:

| Check | Result |
|---|---|
| `dotnet restore PNet.Mesh.sln --verbosity minimal` | passed |
| `dotnet package list --project PNet.Mesh.sln --vulnerable --include-transitive` | no vulnerable packages |
| `dotnet package list --project PNet.Mesh.sln --deprecated --include-transitive` | no deprecated packages |
| `dotnet package list --project PNet.Mesh.sln --outdated` | no direct package updates |
| `dotnet package list --project PNet.Mesh.sln --outdated --include-transitive` | transitive-only updates remain |

No package files were changed.
