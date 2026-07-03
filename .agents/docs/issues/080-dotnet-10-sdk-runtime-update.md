---
issue: 080
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

# 080 - .NET 10 SDK Runtime Update

## Description

The refine version check found the host .NET 10 SDK/runtime behind the currently available Fedora 43 packages. The project builds on SDK `10.0.108` / runtime `10.0.8`, while Fedora updates offers SDK `10.0.109` and runtime `10.0.9`.

## Scope

- Update the local Fedora .NET packages when host package updates are allowed.
- Keep the project on `net10.0`; no target-framework migration is needed.
- Re-run restore, Release build, unit tests, TUN unit tests, e2e batches, format verification, and NuGet health checks after the host update.
- Update `~/.agents/docs/tools/dotnet-10.md` installed-version fields if the host update is applied.

## Acceptance Criteria

- `dotnet --info` reports SDK `10.0.109` and runtime `10.0.9` or newer from the Fedora packages.
- `dotnet restore PNet.Mesh.sln` passes.
- `dotnet build PNet.Mesh.sln -c Release --no-restore` passes.
- The README-documented unit, TUN unit, and bounded Testcontainers e2e commands pass or have a documented host/environment blocker.
- `dotnet format whitespace PNet.Mesh.sln --include <touched paths> --no-restore --verify-no-changes --verbosity minimal` passes after any related edits.

## Research

- `dotnet --info` on 2026-07-03 reported SDK `10.0.108`, `Microsoft.NETCore.App` `10.0.8`, and `Microsoft.AspNetCore.App` `10.0.8`.
- Microsoft .NET 10 release metadata on 2026-07-03 reported latest release `10.0.9`, latest runtime `10.0.9`, latest SDK `10.0.301`, active support, and EOL `2028-11-14`.
- `dnf -q repoquery --latest-limit=1 --qf '%{version}' dotnet-sdk-10.0` reported Fedora package version `10.0.109`.
- `dnf -q check-update dotnet-sdk-10.0 dotnet-runtime-10.0 aspnetcore-runtime-10.0` reported available updates for SDK `10.0.109-1.fc43` and runtime packages `10.0.9-1.fc43`.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The current host has .NET SDK `10.0.108` and runtime `10.0.8` installed. | verified | test | `dotnet --info` reported SDK `10.0.108`, `Microsoft.NETCore.App` `10.0.8`, and `Microsoft.AspNetCore.App` `10.0.8` on 2026-07-03. |
| 2 | F | Fedora 43 updates offers `dotnet-sdk-10.0` version `10.0.109` and runtime packages `10.0.9`. | verified | test | `dnf repoquery` and `dnf check-update` reported SDK `10.0.109-1.fc43`, `dotnet-runtime-10.0` `10.0.9-1.fc43`, and `aspnetcore-runtime-10.0` `10.0.9-1.fc43`. |
| 3 | F | Microsoft .NET 10 remains active LTS with EOL `2028-11-14`. | verified | source | `~/.agents/docs/tools/dotnet-10.md` was revalidated against Microsoft release metadata on 2026-07-03. |

## Completion Report

Pending.
