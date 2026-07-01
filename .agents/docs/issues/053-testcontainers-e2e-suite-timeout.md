---
issue: 053
date: 2026-07-01
source: testing/e2e
priority: medium
status: completed
research-status: complete
research-date: 2026-07-01
terminal-state: completed
completed-date: 2026-07-01
completed-commits:
  - 9ce2d5e
assumptions-date: 2026-07-01
brief: "description+scope+acceptance-criteria"
views:
  fix: "description+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 053 - Testcontainers E2E Suite Timeout

## Description

The documented full Testcontainers e2e command timed out under its 420s cap while verifying #042:

`timeout 420s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none`

The run completed several topology groups and cleaned their containers/images, then reached the timeout while preparing another group. The output showed repeated Docker image builds for `localhost/pnet-mesh-test-node:<runid>` between groups.

## Scope

- In scope: split e2e runs into documented batches, prebuild/reuse the TestNode image across tests, tune test-map/tag selection, or raise the documented timeout with evidence.
- Out of scope: changing mesh behavior, weakening assertions, hiding failing test output, or removing coverage.

## Acceptance Criteria

- The standard e2e verification path is deterministic and finishes inside its documented timeout, or the documentation is updated to a measured timeout that matches reality.
- The suite avoids unnecessary per-test image rebuilds when tests share the same TestNode image inputs.
- Failure output identifies the last test or topology group clearly enough to resume a targeted rerun.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The full e2e command can exceed the documented 420s timeout without an assertion failure. | verified | test | On 2026-07-01, the command timed out with exit code 124 after multiple container groups completed and cleaned up. |
| 2 | F | Repeated TestNode image builds contribute materially to full-suite runtime. | verified | test | The timed-out run logged multiple distinct `Docker image localhost/pnet-mesh-test-node:<runid> built` events before timeout. |

## Completion Report

Implemented in `9ce2d5e`.

- Reworked `PNetMeshTestNodeHarness` to build one process-scoped TestNode image per e2e run, guarded by a static `SemaphoreSlim` and shared image task.
- Kept per-harness isolation for containers and Docker networks.
- Cleared the shared image task after failed or canceled builds so later tests can retry.
- Added a lightweight regression test proving harness instances share the TestNode image name.

Verification:
- Regression build failed before implementation because `TestNodeImageName` did not exist.
- `dotnet build src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-restore` passed with 0 warnings.
- `dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.test_node_image_name_is_shared_across_harness_instances` passed: 1 test.
- `timeout 420s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none` passed: 14 tests in 409.167s; output showed a single TestNode image build.
- `dotnet build PNet.Mesh.sln -c Release --no-restore` passed with 0 warnings.
- Scoped `dotnet format whitespace ... --verify-no-changes` passed.
- Post-run Docker image check found no lingering `localhost/pnet-mesh-test-node:*` image.
- Testing review: `TESTING CLEAR`.
- Surveyor review: `SURVEYOR APPROVED`.

## Completion Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The #053 source implementation is contained in commit `9ce2d5e`. | verified | source | `git log --oneline -4` reported `9ce2d5e test: reuse TestNode image in e2e harness`. |
| 2 | F | The documented full Testcontainers e2e command completed inside the documented 420s timeout after the change. | verified | test | The command passed 14 tests in 409.167s. |
| 3 | F | The run avoided repeated TestNode image builds. | verified | test | The full e2e output contained one `Docker image localhost/pnet-mesh-test-node:<id> built` line. |
