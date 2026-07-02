---
issue: 070
date: 2026-07-02
source: testing/e2e
priority: medium
status: ready
assumptions-date: 2026-07-02
brief: "description+scope+acceptance-criteria"
views:
  fix: "description+scope+acceptance-criteria+assumptions"
---

# 070 - Testcontainers E2E Full Suite Timeout Recurrence

## Description

The documented full Testcontainers e2e command exceeded the 420s timeout again during #059 verification:

```bash
timeout 420s dotnet run --project src/PNet.Mesh.E2ETests/PNet.Mesh.E2ETests.csproj -c Release --no-build -- -parallel none
```

The run first reported `six_node_topology_matches_compose_smoke_route_with_docker_dns_aliases` missing the expected `ping from node20 to node00` log entry, then continued through later groups and timed out before the final xUnit summary. A targeted rerun of that named method passed 1/1 in 112.602s, so the failure appears timing-sensitive rather than a deterministic #059 regression.

## Scope

- Make the full e2e command deterministic inside the documented timeout, or split/document the full suite into bounded batches.
- Preserve the six-node topology assertion and failure diagnostics.
- Add enough timing/log evidence to distinguish assertion failures from suite-duration failures.
- Keep existing Testcontainers cleanup behavior.

## Acceptance Criteria

- The documented e2e verification path completes under its stated timeout on the current host class, or documentation is updated with measured batch commands and timeout boundaries.
- The six-node DNS-alias topology is either stable in full-suite order or has a focused mitigation for its timing-sensitive route assertion.
- Timeout/failure output identifies the last running test and any missing expected log entries.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The full e2e command timed out at 420s during #059 verification. | verified | test | Command exited 124 on 2026-07-02 after running multiple Testcontainers groups. |
| 2 | F | The full run reported a six-node DNS-alias missing-log failure before timeout. | verified | test | Output reported missing `ping from node20 to node00` for `six_node_topology_matches_compose_smoke_route_with_docker_dns_aliases`. |
| 3 | F | The named six-node DNS-alias method passed when rerun by itself. | verified | test | `timeout 420s dotnet run ... -method PNet.Actor.E2ETests.Mesh.PNetMeshTestNodeHarnessTests.six_node_topology_matches_compose_smoke_route_with_docker_dns_aliases -parallel none` passed 1/1 in 112.602s. |
