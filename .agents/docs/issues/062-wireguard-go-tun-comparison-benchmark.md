---
issue: 062
date: 2026-07-02
source: benchmark/integration-phase-3
priority: medium
status: completed
terminal-state: completed
gate-last-checked: 2026-07-02
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
brief: "description+playbook+scope+acceptance-criteria+gate"
views:
  enrich: "description+playbook+scope+gate+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+gate+assumptions"
  complete: "description+completion-report+resolving-commits"
completed-commits: efecd4ad74c85688ba2836a390eb55e902f18040
completed-date: 2026-07-02
---

# 062 - wireguard-go TUN Comparison Benchmark

## Description

Add an equivalent `wireguard-go` benchmark baseline that uses the same topology, MTU, addresses, traffic tools, and run profile as the PNet.Mesh.Tun benchmark. This gives the project a grounded latency and bandwidth comparison against a userspace WireGuard implementation.

## Playbook

- `Fairness`: use the same topology, route layout, MTU, duration, traffic profile, and host metadata as PNet.Mesh.Tun runs.
- `Separation`: keep PNet.Mesh.Tun and `wireguard-go` runs separate so processes, routes, and counters do not overlap.
- `Reproducibility`: pin or record the exact `wireguard-go` version/source and setup command used.
- `Metrics`: collect latency, bandwidth, CPU, RSS, process count, and tool output; .NET allocation counters apply only to PNet.Mesh runs.

## Scope

- Add a `wireguard-go` setup path for the benchmark topology from #060.
- Run IPv4 and IPv6 `ping` latency probes over `wireguard-go`.
- Run IPv4 and IPv6 `iperf3` bandwidth probes over `wireguard-go`.
- Record `wireguard-go` version, command line, configuration, keys, MTU, and environment metadata.
- Ensure teardown fully removes the `wireguard-go` interfaces, processes, addresses, and routes.

## Out Of Scope

- Changing the PNet.Mesh.Tun benchmark scenario.
- Claiming protocol parity beyond the measured benchmark profile.
- Applying .NET allocation metrics to the `wireguard-go` process.

## Acceptance Criteria

- `wireguard-go` latency and bandwidth runs use the same benchmark topology and traffic profile as PNet.Mesh.Tun.
- Results are machine-readable and include version, configuration, MTU, addresses, duration, and host metadata.
- Teardown leaves no benchmark interfaces, routes, or `wireguard-go` processes.
- Documentation states which metrics are directly comparable and which are runtime-specific.

## Gate

Cleared. #061 added and stabilized the PNet.Mesh.Tun benchmark scenario in `53ca5bb` and `d667db3`. The shared topology from #060 is complete in `1e079d2`.

## Gate Validation

| Date | Gate | Method | Result | Evidence |
|------|------|--------|--------|----------|
| 2026-07-02 | `gate-depends: [060, 061]` | source | partially passed | #060 is complete in `1e079d2`; #061 remains open, so #062 stays gated only on #061. |
| 2026-07-02 | `gate-depends: [060, 061]` | source | passed | #060 is complete in `1e079d2`; #061 is complete in `53ca5bb` and `d667db3`, so #062 is ready. |

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The user wants PNet.Mesh performance compared with `wireguard-go`. | verified | source | The user asked how to compare allocation, performance, latency, and bandwidth with `wireguard-go`. |
| 2 | R | A fair comparison needs the same topology, MTU, traffic profile, and environment metadata. | verified | logical | Changing any of those variables would mix benchmark target differences with test setup differences. |
| 3 | F | Runtime-specific allocation counters are only meaningful for PNet.Mesh/.NET runs. | verified | source | Existing benchmark issues define .NET allocation and GC counters for PNet.Mesh benchmark output. |

## Completion Report

Implemented in `efecd4a`.

- Added the `wireguard-go` benchmark baseline on the shared TUN topology from #060.
- The benchmark uses the same traffic profile as PNet.Mesh.Tun and records version, configuration, MTU, addresses, environment metadata, and process metrics.
- The final `wireguard-go` and `pnet-mesh-tun` benchmark runs both passed with clean teardown.

Verification:

- `dotnet build PNet.Mesh.sln -c Release --no-restore` passed.
- `PNet.Mesh.Tun.UnitTests` passed 12/12.
- Docker image build passed.
- `wireguard-go` benchmark status passed with IPv4/IPv6 ping and `iperf3` plus process metrics.
- `pnet-mesh-tun` benchmark status passed after the shared runner change.
- Format and diff check passed.
- Docker labeled cleanup was empty.

## Resolving Commits

- `efecd4ad74c85688ba2836a390eb55e902f18040` - benchmarks: add wireguard-go TUN baseline
