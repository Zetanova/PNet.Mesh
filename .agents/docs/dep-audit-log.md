---
last-audit: 2026-07-03
last-refined: 2026-07-03
---

# Dependency Audit Log

NuGet audit history for this project.

| Date | Ecosystem | Command | Result | Notes |
|------|-----------|---------|--------|-------|
| 2026-07-03 | .NET/NuGet | `dotnet restore PNet.Mesh.sln` | pass | Restored 9 projects with 0 warnings and 0 errors. |
| 2026-07-03 | .NET/NuGet | `dotnet package list --project <SDK project> --vulnerable --include-transitive --no-restore` | pass | All scanned SDK projects report no vulnerable packages from configured sources. |
| 2026-07-03 | .NET/NuGet | `dotnet package list --project <SDK project> --deprecated --no-restore` | pass | All scanned SDK projects report no deprecated packages from configured sources. |
| 2026-07-03 | .NET/NuGet | `scripts/packages.sh --dry-run --timeout 60` | action | Found `Testcontainers` `4.12.0 -> 4.13.0`; tracked in issue #082. |
| 2026-06-30 | .NET/NuGet | `dotnet list PNet.Mesh.sln package --vulnerable --include-transitive` | inconclusive | Restore failed with `NU1102` for `Noise` `1.0.0-pre-201227`. |
| 2026-06-30 | .NET/NuGet | `dotnet list PNet.Mesh.sln package --deprecated` | inconclusive | Restore failed with `NU1102` for `Noise` `1.0.0-pre-201227`. |
| 2026-06-30 | .NET/NuGet | `dotnet list PNet.Mesh.sln package --outdated --include-prerelease` | inconclusive | Restore failed with `NU1102` for `Noise` `1.0.0-pre-201227`. |
| 2026-06-30 | .NET/NuGet | `dotnet restore PNet.Mesh.sln` | pass | Restored `net10.0` projects with `Noise.NET` `1.0.0`. |
| 2026-06-30 | .NET/NuGet | `dotnet package list --project <SDK project> --vulnerable --include-transitive --no-restore` | pass | `PNet.Mesh`, `PNet.Mesh.UnitTests`, and `PNet.Mesh.TestNode` report no vulnerable packages. |
| 2026-06-30 | .NET/NuGet | `dotnet package list --project <SDK project> --deprecated --no-restore` | pass | `PNet.Mesh`, `PNet.Mesh.UnitTests`, and `PNet.Mesh.TestNode` report no deprecated packages. |
| 2026-06-30 | .NET/NuGet | `dotnet package list --project <SDK project> --outdated --no-restore` | pass | `PNet.Mesh`, `PNet.Mesh.UnitTests`, and `PNet.Mesh.TestNode` report no newer package versions. |
