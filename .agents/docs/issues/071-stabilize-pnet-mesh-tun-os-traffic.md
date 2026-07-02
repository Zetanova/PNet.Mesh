---
issue: 071
date: 2026-07-02
source: benchmark/tun-stability
priority: medium
status: completed
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - d667db3
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

## Completion Report

Implemented in `d667db3`.

- Fixed the Linux TUN device read path by disabling `FileStream` buffering for packet-device reads, which removed delayed small-packet bursts.
- Preserved TUN burst delivery without blocking the only TUN reader on one slow peer by adding per-peer bridge send queues and per-send unreliable mesh delivery.
- Hardened mesh channel relay processing with a bounded relay worker, wakeable pending-relay queue, queued-relay cancellation handling, and regression coverage for cancellation behind a pending relay.
- Reduced benchmark false positives: final status now requires loss-free ping and positive `iperf3` bandwidth, ping probes have bounded deadlines, and `iperf3` readiness verifies the exact IPv4/IPv6 bind address.
- Installed `iproute2` in the test-node image so `ss`-based readiness checks run in containerized e2e and benchmark flows.
- Kept benchmark reports recording process RSS/CPU metrics and explicit managed-counter unavailability.

Verification on 2026-07-02:

- `timeout 180s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` passed with 0 warnings.
- `timeout 240s rtk dotnet run --project src/PNet.Mesh.UnitTests/PNet.Mesh.UnitTests.csproj -c Release --no-build -- -parallel none` passed, 175/175 tests.
- `timeout 240s rtk dotnet run --project src/PNet.Mesh.Tun.UnitTests/PNet.Mesh.Tun.UnitTests.csproj -c Release --no-build -- -parallel none` passed, 10/10 tests.
- All five bounded Testcontainers e2e batches passed: 4/4, 3/3, 4/4, 2/2, and 1/1 tests.
- `timeout 420s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-benchmark pnet-mesh-tun --ping-count 1 --iperf-duration 3s` returned `status: pass`; IPv4/IPv6 ping succeeded, IPv4/IPv6 `iperf3` exited 0 with zero UDP loss, and process metrics were present.
- `docker ps -aq --filter label=pnet.mesh.benchmark.topology=pnet-tun-bench` and `docker network ls -q --filter label=pnet.mesh.benchmark.topology=pnet-tun-bench` returned no resources after the benchmark.

## Resolving Commits

- `d667db3` - `benchmarks: stabilize PNet.Mesh.Tun traffic`

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The initial PNet.Mesh.Tun benchmark command failed OS traffic in this environment. | verified | test | On 2026-07-02, after rebuilding `localhost/pnet-mesh-tun:dev`, `timeout 180s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-benchmark pnet-mesh-tun --ping-count 1 --warmup 0ms --iperf-duration 1s --timeout 15s` wrote `/tmp/pnet-tun-benchmark.json` with `status: fail`; IPv4 ping received 1/9 packets, IPv6 ping lost 10/10 packets, IPv4 `iperf3` exited 1, and IPv6 `iperf3` failed readiness. |
| 2 | F | Both benchmark TUN processes remained available while traffic failed. | verified | test | The same report recorded `/proc` metrics for `pnet-tun-bench-left` and `pnet-tun-bench-right`, including PID, RSS, thread count, and CPU ticks. |
| 3 | F | The failed benchmark run cleaned up the labeled Docker topology. | verified | test | The report's teardown status was `pass`, and `docker ps -a` plus `docker network ls` with label `pnet.mesh.benchmark.topology=pnet-tun-bench` returned no resources. |
| 4 | F | #061 cannot complete until IPv4/IPv6 ping and `iperf3` pass over PNet.Mesh.Tun. | verified | source | #061 acceptance requires machine-readable IPv4/IPv6 latency and `iperf3` bandwidth results. |
| 5 | F | The 2026-07-02 team-task partial fixes did not satisfy #071 acceptance before the TUN stream buffering fix. | verified | test | After rebuilding `localhost/pnet-mesh-tun:dev`, `/tmp/codex-team-task.uXegtj/test/local-071-benchmark-4.json` returned `status: fail`; IPv4 and IPv6 ping each transmitted 1 and received 0, both `iperf3` probes timed out, both TUN processes stayed available, and labeled teardown removed containers and network. |
| 6 | F | Buffering the Linux TUN `FileStream` by MTU caused small OS packets to flush in delayed bursts. | verified | test | Repeated benchmark logs showed peer-to-TUN packet batches released only after enough small ping/iperf packets accumulated; changing `LinuxTunDevice` to use `bufferSize: 1` removed the delayed bursts. |
| 7 | F | The PNet.Mesh.Tun benchmark now satisfies #071 traffic acceptance in this environment. | verified | test | On 2026-07-02, `timeout 180s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-benchmark pnet-mesh-tun --name pnet-tun-bench --ping-count 1 --warmup 0ms --iperf-duration 1s --timeout 30s` returned `status: pass`; IPv4 ping averaged 2.172 ms, IPv6 ping averaged 1.975 ms, IPv4 and IPv6 `iperf3` returned exit code 0 with zero lost UDP packets, and teardown removed the labeled topology. |
| 8 | F | The final post-review PNet.Mesh.Tun benchmark still satisfies #071 traffic acceptance. | verified | test | On 2026-07-02, `timeout 420s rtk dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --tun-benchmark pnet-mesh-tun --ping-count 1 --iperf-duration 3s` returned `status: pass`; IPv4 ping averaged 25.486 ms, IPv6 ping averaged 19.475 ms, IPv4 `iperf3` reported 1015.7356362846427 bits/s received, IPv6 `iperf3` reported 1022.4431598818866 bits/s received, both reported zero lost UDP packets, process metrics were present, managed counters were explicitly unavailable, and labeled Docker cleanup removed containers and network. |
