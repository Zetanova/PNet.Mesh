---
issue: 002
date: 2026-06-30
source: test-output-analysis
priority: high
status: completed
terminal-state: extended-clarify
clarify-prompt: "Should the project restore `Noise` from an additional package source, change to an available package/version, or replace the dependency during the .NET migration?"
research-date: 2026-06-30
research-status: complete
completion-date: 2026-06-30
commits: [30ea5f8]
assumptions-date: 2026-06-30
---

# 002 - Noise Package Restore Blocker

## Description

`dotnet restore PNet.Mesh.sln` fails because NuGet cannot find `Noise` version `1.0.0-pre-201227` in the configured feeds. This blocks restore, package vulnerability/deprecation/outdated scans, build, tests, and Docker image builds.

Do not guess a crypto package replacement. The dependency is used in handshake/protocol code, so the next step needs a user decision or dedicated migration research.

## Playbook

- `Restore/audit flow`: developer runs `dotnet restore PNet.Mesh.sln`; NuGet resolves all PackageReferences; then audit, build, and tests can execute.
- `Protocol dependency flow`: `PNetMeshProtocol` parses and uses the Noise protocol implementation; package changes must preserve handshake behavior.
- `Gap`: clarify whether to add a package source, retarget the available package/version, or replace the dependency during issue #001.

## Research

### Root Cause

The project references `Noise` `1.0.0-pre-201227`, but configured sources only returned `Noise` `0.1.1.1` as the nearest version.

### Resolution Approach

Clarify the intended package source/version first. After that, run restore and package audits again before any build/test claim.

### Code References

- `src/PNet.Mesh/PNet.Mesh.csproj`
- `src/PNet.Mesh/PNetMeshProtocol.cs`
- `tests/PNet.Mesh.UnitTests/PNetMeshProtocolTest.cs`
- `tests/PNet.Mesh.UnitTests/PNetMeshServerTests.cs`

### Edge Cases & Risks

Changing a cryptographic protocol dependency can alter handshake semantics, packet compatibility, or test vectors.

### Complexity Estimate

S/M - small if the missing feed is known; medium if replacement or protocol migration is required.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `PNet.Mesh.csproj` references `Noise` `1.0.0-pre-201227`. | verified | source | `src/PNet.Mesh/PNet.Mesh.csproj` contains that PackageReference. |
| 2 | F | Restore fails because the configured feeds do not provide that `Noise` version. | verified | test | `dotnet restore PNet.Mesh.sln` failed with `NU1102` and nearest version `0.1.1.1`. |
| 3 | F | The configured NuGet sources are `viva` and `avon`. | verified | test | `dotnet nuget list source` listed those two enabled sources. |
| 4 | F | `Noise` is used by protocol and test code. | verified | source | `PNetMeshProtocol.cs` and multiple unit tests import `Noise`. |

## Completion Report

Completed in `30ea5f8`.

- Replaced unavailable `Noise` `1.0.0-pre-201227` with `Noise.NET` `1.0.0`.
- Updated `PNetMeshProtocol` to preserve explicit packet counters with the available `Noise.NET` transport API.
- Verified restore, clean build, protocol/server unit tests, package audits, and Docker e2e smoke.
