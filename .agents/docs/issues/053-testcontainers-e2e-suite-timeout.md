---
issue: 053
date: 2026-07-01
source: testing/e2e
priority: medium
status: ready
research-status: complete
research-date: 2026-07-01
terminal-state: ready
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
