---
issue: 051
date: 2026-07-01
source: nuget/audit
priority: medium
status: gated
terminal-state: gated
gate: "Run in a dedicated dependency update session; this is out of scope for the WireGuard crypto-profile implementation."
gate-reason: "The finding concerns transitive packages across multiple direct dependencies and needs normal dependency-update review rather than an incidental crypto change."
research-status: complete
assumptions-date: 2026-07-01
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

This issue is gated on a dedicated dependency update session. The current finding is not a blocker for the WireGuard BLAKE2s implementation because vulnerable and deprecated checks reported no findings.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The solution restore succeeded before package health checks ran. | verified | test | `dotnet restore PNet.Mesh.sln --verbosity minimal` completed successfully on 2026-07-01. |
| 2 | F | The dependency audit found no vulnerable packages in the solution. | verified | test | `dotnet package list --project PNet.Mesh.sln --vulnerable --include-transitive` reported no vulnerable packages for all four projects. |
| 3 | F | The dependency audit found no deprecated packages in the solution. | verified | test | `dotnet package list --project PNet.Mesh.sln --deprecated --include-transitive` reported no deprecated packages for all four projects. |
| 4 | F | The dependency audit reported transitive package updates across the solution. | verified | test | `dotnet package list --project PNet.Mesh.sln --outdated --include-transitive` reported updates including `NETStandard.Library`, `Microsoft.NETCore.Platforms`, `Microsoft.Testing.Platform`, `Docker.DotNet.Enhanced`, and `Newtonsoft.Json`. |
