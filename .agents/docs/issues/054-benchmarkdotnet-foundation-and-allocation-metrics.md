---
issue: 054
date: 2026-07-02
source: benchmark/phase-1
priority: medium
status: completed
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
terminal-state: completed
completed-date: 2026-07-02
completed: 2026-07-02
completed-commits:
  - a402a8afe02f9f0d90edd9eb6adc1cae616ebbc6
brief: "description+scope+acceptance-criteria+assumptions+playbook"
views:
  enrich: "description+scope+acceptance-criteria+assumptions+playbook"
  fix: "description+scope+acceptance-criteria+assumptions+playbook"
  complete: "description+completion-report"
---

# 054 - BenchmarkDotNet Foundation And Allocation Metrics

## Description

PNet.Mesh needs a dedicated Release benchmark project before optimization work can be evidence-driven. Create a BenchmarkDotNet project for protocol hot paths, wire it into the solution, and document canonical commands and artifacts.

## Playbook

- `Project boundary`: keep benchmark code in `src/PNet.Mesh.Benchmarks` and reference `src/PNet.Mesh/PNet.Mesh.csproj`.
- `Configuration`: run benchmarks in Release only and fail or warn clearly when the configuration is Debug.
- `Metrics`: enable `MemoryDiagnoser` on every benchmark class so allocation columns are always present.
- `Matrix`: benchmark payload sizes 64, 128, 512, 1280, and 1420 with `ns/op`, `ops/sec`, `B/op`, `Gen0`, `Gen1`, and `Gen2`.
- `Artifacts`: document the restore, build, and benchmark commands plus the output path for reports and exports.

## Scope

- Create `src/PNet.Mesh.Benchmarks` and reference `src/PNet.Mesh/PNet.Mesh.csproj`.
- Add BenchmarkDotNet with `MemoryDiagnoser` enabled for every benchmark class.
- Document Release-only benchmark commands and artifact output paths.
- Define the initial matrix: payload sizes 64, 128, 512, 1280, 1420; metrics `ns/op`, `ops/sec`, `B/op`, `Gen0`, `Gen1`, `Gen2`.
- Fail or warn clearly when benchmarks run in Debug configuration.

## Out of Scope

- Optimizing code paths before baseline data exists.
- Adding Docker/Testcontainers benchmarks to the BenchmarkDotNet project.

## Acceptance Criteria

- `dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --filter '*'` runs at least one benchmark.
- Every benchmark report includes allocation columns: `Allocated`, `Gen0`, `Gen1`, and `Gen2`.
- README or project docs include the canonical restore, build, and run commands.
- Benchmark artifacts are written to a documented path and are easy to archive or compare.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The solution currently has no benchmark project. | verified | source | The current solution inventory contains Mesh, UnitTests, TestNode, and E2ETests only. |
| 2 | F | BenchmarkDotNet can report allocation metrics with MemoryDiagnoser. | verified | source | BenchmarkDotNet `MemoryDiagnoser` reports allocation and GC columns. |
| 3 | F | PNet.Mesh benchmarks should run against Release builds. | verified | source | Project best-practices use Release build and test commands. |

## Completion Report

Implemented in `a402a8a`.

- Added a BenchmarkDotNet benchmark project and wired it into the solution.
- Added Release-only benchmark documentation and canonical commands.
- Added the payload-size matrix and allocation/GC reporting columns.

Verification:
- `dotnet build PNet.Mesh.sln -c Release --no-restore` passed.
- `dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release -- --filter '*'` passed and produced `Gen1`, `Gen2`, `Op/s`, `Gen0`, and `Allocated` columns.
- `dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` passed 154 tests.

## Resolving Commits

- `a402a8afe02f9f0d90edd9eb6adc1cae616ebbc6` - benchmarks: add BenchmarkDotNet foundation
