---
issue: 071
date: 2026-07-02
source: benchmark/tun-stability
priority: medium
status: ready
terminal-state: ready
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
brief: "description+playbook+scope+acceptance-criteria+assumptions"
views:
  enrich: "description+playbook+scope+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report+resolving-commits"
---

# 071 - Stabilize PNet.Mesh.Tun OS Traffic

## Description

PNet.Mesh.Tun can start inside the privileged benchmark topology, but normal OS traffic over the TUN path is not stable enough for #061. The current diagnostic benchmark records both TUN processes as alive, captures bridge forwarding logs, and tears down cleanly, yet IPv4/IPv6 ping and `iperf3` fail.

## Playbook

- `Reproduce`: run `--tun-benchmark pnet-mesh-tun --ping-count 1 --warmup 0ms --iperf-duration 1s --timeout 15s` after Release build.
- `Evidence`: inspect `/tmp/pnet-tun-benchmark.json` traffic, process metrics, and captured `/tmp/pnet-tun.log` tails.
- `Scope`: fix the PNet.Mesh.Tun OS packet path, route handling, peer session behavior, or benchmark harness only where evidence shows the harness is wrong.
- `Regression`: add automated coverage that fails on the observed sustained ping/`iperf3` behavior, using disposable privileged containers when host TUN access is required.
- `Cleanup`: every repro path must leave no Docker resources labeled `pnet.mesh.benchmark.topology=pnet-tun-bench`.

## Scope

- Diagnose why ICMP and UDP traffic forwarded by `PNetMeshTunBridge` does not reach normal OS tools reliably.
- Fix the root cause in PNet.Mesh.Tun, PNet.Mesh session routing, or the benchmark topology/runner as evidence requires.
- Preserve the #060 labeled topology lifecycle and the #061 diagnostic JSON output shape unless the output is proven wrong.
- Keep generated keys and PSKs out of persisted benchmark command logs.

## Out Of Scope

- Adding the `wireguard-go` comparison baseline.
- Defining regression thresholds for performance.
- Optimizing allocation hotspots unrelated to packet delivery.

## Acceptance Criteria

- The PNet.Mesh.Tun benchmark command returns `status: pass` for IPv4 ping, IPv6 ping, IPv4 `iperf3`, and IPv6 `iperf3` in the privileged topology.
- A regression test or documented disposable-container probe covers the sustained OS traffic path that failed here.
- The benchmark report still records process RSS/CPU metrics and reports managed counters as available or explicitly unavailable.
- Teardown removes all labeled containers and networks after passing and failing benchmark runs.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The current PNet.Mesh.Tun benchmark command fails OS traffic in this environment. | verified | test | On 2026-07-02, after rebuilding `localhost/pnet-mesh-tun:dev`, `timeout 180s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-benchmark pnet-mesh-tun --ping-count 1 --warmup 0ms --iperf-duration 1s --timeout 15s` wrote `/tmp/pnet-tun-benchmark.json` with `status: fail`; IPv4 ping received 1/9 packets, IPv6 ping lost 10/10 packets, IPv4 `iperf3` exited 1, and IPv6 `iperf3` failed readiness. |
| 2 | F | Both benchmark TUN processes remained available while traffic failed. | verified | test | The same report recorded `/proc` metrics for `pnet-tun-bench-left` and `pnet-tun-bench-right`, including PID, RSS, thread count, and CPU ticks. |
| 3 | F | The failed benchmark run cleaned up the labeled Docker topology. | verified | test | The report's teardown status was `pass`, and `docker ps -a` plus `docker network ls` with label `pnet.mesh.benchmark.topology=pnet-tun-bench` returned no resources. |
| 4 | F | #061 cannot complete until IPv4/IPv6 ping and `iperf3` pass over PNet.Mesh.Tun. | verified | source | #061 acceptance requires machine-readable IPv4/IPv6 latency and `iperf3` bandwidth results. |
