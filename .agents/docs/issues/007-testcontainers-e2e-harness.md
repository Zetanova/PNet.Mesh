---
issue: 007
date: 2026-06-30
source: e2e/testcontainers
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-06-30
commits: [52cf1f7]
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 007 - Add Testcontainers E2E Harness

## Description

Create a dedicated xUnit e2e test project that can build or reuse the PNet.Mesh test-node image, create isolated Docker networks, start node containers with deterministic options, collect logs on failure, and dispose resources through the test lifecycle.

## Playbook

- `E2E test project`: test runner starts containers from C#; expected result is one command integrated with `dotnet test` or `dotnet run`.
- `Topology builder`: test code describes nodes, keys, peers, networks, expected routes, and runtime timing without YAML duplication.
- `Failure diagnostics`: failed scenario prints container status and relevant node logs.

## Scope

- Add a new e2e test project and include it in the solution.
- Add Testcontainers package references through the repo package maintenance path.
- Provide reusable helpers for node image build, network creation, container startup, wait conditions, log collection, and cleanup.
- Keep the first scenario minimal; topology parity is child #008.

## Acceptance Criteria

- E2E harness starts at least one `PNet.Mesh.TestNode` container and asserts readiness.
- Failed startup emits actionable node logs.
- The harness has bounded timeouts and cleans up containers and networks.
- Documentation includes the e2e command.

## Research

### Open Questions

- Verify the exact Testcontainers .NET API surface for Dockerfile image builds, UDP ports, network aliases, and xUnit v3 fixtures before implementation.
- Decide whether e2e tests should run through `dotnet test` or the current xUnit console-compatible `dotnet run` pattern.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | The repo uses xUnit v3 packages in the current unit test project. | verified | source | `src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj` references `xunit.v3`. |
| 2 | F | The existing test-node project can run as a container entrypoint. | verified | source | `src/PNet.Mesh.TestNode/Dockerfile` publishes and runs `PNet.Mesh.TestNode.dll`. |
| 3 | F | The exact Testcontainers API usage for this repo has been verified from the cached package metadata. | verified | source | Testcontainers 4.12.0 exposes Dockerfile image build, xUnit-compatible use, network, alias, port binding, and UDP APIs in the net10.0 package docs. |

## Enrichment History

- 2026-06-30: Marked ready after confirming Testcontainers 4.12.0 restores on net10.0 and exposes Dockerfile image-build, network, alias, port-binding, and UDP APIs. Evidence: the cached package metadata and XML docs under `~/.nuget/packages/testcontainers/4.12.0/`.

## Completion Report

Completed in `52cf1f7`.

- Added a dedicated xUnit v3 Testcontainers e2e project to the solution.
- Added a reusable test-node harness that builds the Dockerfile from the repository context, creates isolated Docker networks, starts node containers, waits for readiness logs, captures failed-startup logs, and performs best-effort cleanup across containers, networks, and generated images.
- Added e2e coverage for successful single-node readiness and failed-startup diagnostics.
- Documented the timeout-wrapped e2e command in the README and project guidance.
- Verified restore, Release build, unit tests, e2e tests, package health checks, scoped whitespace formatting, and `git diff --check`; testing, container, config, and doc-sync reviewers approved the change.

## Resolving Commits

- `52cf1f75cbe19142b3c83952b081bdad186122a8` - add Testcontainers e2e harness
