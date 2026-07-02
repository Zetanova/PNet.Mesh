---
issue: 060
date: 2026-07-02
source: benchmark/integration-phase-1
priority: medium
status: completed
gate-last-checked: 2026-07-02
probeable: false
research-status: complete
research-date: 2026-07-02
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - 1e079d2
assumptions-date: 2026-07-02
brief: "description+playbook+scope+acceptance-criteria+gate"
views:
  enrich: "description+playbook+scope+gate+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+gate+assumptions"
  complete: "description+completion-report+resolving-commits"
---

# 060 - Privileged TUN Benchmark Topology

## Description

Define the privileged Linux namespace or container topology used by PNet.Mesh.Tun integration benchmarks. This is the first integration-benchmark slice after the optional TUN component exists and should establish repeatable setup, teardown, routing, and preflight behavior before benchmark traffic is added.

## Playbook

- `Isolation`: use disposable Linux network namespaces or containers so interface, address, and route mutations do not touch the host network.
- `Preflight`: fail fast when TUN, namespace, route, or required privilege support is unavailable.
- `Parity`: design the topology so PNet.Mesh.Tun and `wireguard-go` can use the same addresses, MTU, traffic endpoints, and duration profile.
- `Cleanup`: remove interfaces, namespaces, routes, and background processes even after failed benchmark runs.

## Scope

- Document and implement the benchmark topology setup and teardown path.
- Define interface names, IPv4/IPv6 addresses, routes, MTU, UDP ports, and process placement.
- Add a preflight command that reports whether the current host/container can run privileged TUN benchmarks.
- Keep the topology Linux-first and explicitly mark it privileged/manual unless a CI-safe privileged runner exists.
- Emit machine-readable environment metadata needed by later benchmark result comparison.

## Out Of Scope

- Running `iperf3` traffic or collecting benchmark numbers.
- Implementing the optional TUN component itself.
- Adding Windows or macOS TUN benchmark support.

## Acceptance Criteria

- A documented command creates and tears down the benchmark topology without leaving namespaces, routes, or processes behind.
- Preflight reports clear `pass`, `skip`, or `fail` state for TUN and privilege requirements.
- The topology reserves equivalent slots for PNet.Mesh.Tun and `wireguard-go` runs.
- IPv4 and IPv6 addressing, route setup, MTU, ports, and cleanup are documented.

## Gate

The #059 gate is cleared by `781084d`, which added the optional `PNet.Mesh.Tun` component, CLI, Dockerfile, and smoke documentation. This issue is ready to implement the reusable privileged topology.

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-02 | `gate-depends: [059]` | source | passed | #059 completed in `781084d`. |

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | #059 tracks adding optional `PNet.Mesh.Tun` support. | verified | source | `.agents/docs/issues/059-add-optional-pnet-mesh-tun-component.md` defines the optional TUN component and benchmark bridge. |
| 2 | F | The benchmark rollout already has foundation, micro, macro, baseline, and optimization tracking issues. | verified | source | Issues #054-#058 cover those benchmark phases. |
| 3 | R | Integration benchmarks that mutate network interfaces need isolated setup and teardown. | verified | logical | TUN, address, and route setup affects OS networking, so disposable namespaces or containers constrain side effects. |

## Completion Report

Implemented in `1e079d2`.

- Added `--tun-topology plan|preflight|create|teardown` to the Release-only benchmark executable.
- The topology JSON defines two privileged Docker container namespaces, `/dev/net/tun`, `NET_ADMIN`, `NET_RAW`, MTU 1280, IPv4/IPv6 addresses, peer routes, PNet.Mesh.Tun UDP ports, `wireguard-go` UDP ports, and reserved `iperf3` traffic endpoints.
- `preflight` reports `pass`, `skip`, or `fail` for Linux, `/dev/net/tun`, Docker, the TUN CLI image, and the privileged tool probe without mutating Docker state.
- `create` starts only labeled Docker resources; `teardown` removes labeled containers and refuses to delete an unowned network.
- README, TUN docs, and project best-practices document the manual privileged topology commands.

Verification:

- `timeout 180s dotnet build PNet.Mesh.sln -c Release --no-restore` passed.
- `timeout 120s dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none` passed: 5/5.
- `timeout 60s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-topology plan` passed and emitted topology JSON.
- `timeout 90s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-topology preflight` passed with status `pass`.
- `timeout 120s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-topology create` passed and created two labeled containers plus one labeled network.
- `timeout 120s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-topology teardown` passed and removed two containers plus the owned network.
- Docker post-checks found no remaining resources with `pnet.mesh.benchmark.topology=pnet-tun-bench`.
- Scoped `dotnet format whitespace` verification passed.

## Completion Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The privileged topology implementation is contained in commit `1e079d2`. | verified | source | `git commit` created `1e079d2 benchmarks: add privileged TUN topology runner`. |
| 2 | F | The current host can run the privileged topology preflight. | verified | test | `--tun-topology preflight` returned JSON status `pass` with Docker 29.4.2 and `/dev/net/tun` available. |
| 3 | F | The topology create/teardown path cleaned up the resources it created. | verified | test | `create` reported two containers and one network; `teardown` removed both containers and the owned network, and Docker post-checks returned no labeled resources. |
