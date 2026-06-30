---
issue: 004
date: 2026-06-30
source: refine-lint-scan
priority: medium
status: completed
completed: 2026-06-30
research-date: 2026-06-30
research-status: complete
terminal-state: completed
assumptions-date: 2026-06-30
---

# 004 - .NET Format Drift

## Description

`dotnet format PNet.Mesh.sln whitespace --no-restore --verify-no-changes --verbosity minimal` reports existing whitespace drift in six C# files and one charset mismatch. This is a formatter-only issue and should be handled with the deterministic format workflow.

Do not mix this with semantic runtime migration changes. Apply formatter output separately, then rerun the same verify command.

## Research

### Root Cause

The repo did not have `.editorconfig` before refine, and the C# files already had mixed whitespace/encoding conventions. The new config aligns to LF plus the dominant UTF-8 BOM convention for C# project/source files.

### Resolution Approach

Run scoped deterministic formatting through `/issue-manager format` or an equivalent format-only change:

```bash
dotnet format PNet.Mesh.sln whitespace --no-restore --verbosity minimal
dotnet format PNet.Mesh.sln whitespace --no-restore --verify-no-changes --verbosity minimal
```

### Code References

- `src/PNet.Mesh/PNetMeshChannel.cs`
- `src/PNet.Mesh/PNetMeshServer.cs`
- `src/PNet.Mesh/PNetMeshUtils.cs`
- `tests/PNet.Mesh.TestNode/NodeService.cs`
- `tests/PNet.Mesh.TestNode/Program.cs`
- `tests/PNet.Mesh.UnitTests/PNetMeshTest.cs`

### Edge Cases & Risks

Keep this as a format-only change. Restore/build are separately blocked by issue #002, so do not claim semantic verification from this issue alone.

### Complexity Estimate

S - deterministic formatter output, but source files are touched.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `dotnet format` can load the solution enough to report whitespace without restore. | verified | test | `dotnet format ... --no-restore --verify-no-changes` produced concrete WHITESPACE/CHARSET diagnostics. |
| 2 | F | Formatting drift is pre-existing source state, not introduced by this refine source edit. | verified | source | Refine did not edit the listed C# source files. |
| 3 | F | C# source files have mixed encoding state. | verified | test | Formatter reports one `CHARSET` issue after `.editorconfig` was aligned to UTF-8 BOM for C# files. |

## Completion Report

- Marked issue #004 complete in the tracker.
- Recorded resolving commits `7e8340f` and `30ea5f8` in the Completed row.
- Added `.editorconfig` and LF normalization in `30ea5f8`.
- The formatter-only workflow was verified by a failing `dotnet format PNet.Mesh.sln whitespace --no-restore --verify-no-changes --verbosity minimal` baseline and a passing rerun after deterministic formatting.

## Resolving Commits

- `7e8340f` - format: apply deterministic source formatting
- `30ea5f8` - migrate to dotnet 10 and add mesh e2e
