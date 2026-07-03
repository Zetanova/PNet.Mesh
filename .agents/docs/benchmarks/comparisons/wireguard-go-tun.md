---
last-refined: 2026-07-03
status: report-only
peer: wireguard-go
scenario: tun-mtu-64k
last-artifact: artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json
---

# WireGuard-Go TUN Comparison

Definition for comparing PNet.Mesh.Tun against `wireguard-go` on the privileged Linux TUN benchmark topology.

## Trigger

Run on a Linux host or runner with Docker, `/dev/net/tun`, `CAP_NET_ADMIN`, and `CAP_NET_RAW` support:

```bash
docker build -f src/PNet.Mesh.Tun.Cli/Dockerfile -t localhost/pnet-mesh-tun:dev .
timeout 900s scripts/bench-tun-comparison.sh --output-dir artifacts/benchmarks/tun-comparison/$(date -u +%Y%m%dT%H%M%SZ) --ping-count 5 --warmup 2s --iperf-duration 3s --mtu 1420 --payload-mode mtu
```

Use `--baseline <previous-comparison.json>` only after a previous comparison artifact is selected for report-only regression output.

## Definition

| Field | Value |
|---|---|
| Name | `pnet-mesh-tun-vs-wireguard-go` |
| Peer | `wireguard-go` |
| Reason | Userspace WireGuard implementation exercising an equivalent TUN topology and traffic profile. |
| Scenario | Dual-container Linux TUN topology, IPv4/IPv6 ping, and UDP `iperf3`. |
| Equivalence | Same topology name, Docker network, container image, MTU, payload mode, ping count, warmup, `iperf3` duration, port, and datagram size. |
| Metrics | Ping latency, packet loss, `iperf3` throughput, RSS/HWM RSS, threads, CPU ticks, and managed-runtime counter availability. |
| Current artifact | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |

## Current Caveats

| Caveat | Effect |
|---|---|
| `iperf3` was rate-limited to `64K`. | Throughput rows can only show parity under the cap, not maximum throughput. |
| PNet.Mesh.Tun is a .NET process; `wireguard-go` is not. | Managed allocation/GC counters are only applicable to PNet.Mesh. |
| RSS includes runtime overhead. | RSS comparison is operationally useful, but it is not pure protocol state size. |
| Process CPU is recorded as ticks from `/proc`. | Treat CPU rows as coarse comparison data until repeated runs establish variance. |

## First Artifact

| Field | PNet.Mesh | wireguard-go |
|---|---|---|
| Run timestamp | `2026-07-02T16:15:28Z` | `2026-07-02T16:15:52Z` |
| Git commit | `d4856b8a706e` | `d4856b8a706e` |
| Version | `1.0.0.0` | `0.0.20230223-1ubuntu0.24.04.2` |
| OS | Fedora Linux 43 (WSL) | Fedora Linux 43 (WSL) |
| Container engine | `29.4.2` | `29.4.2` |
| MTU | `1420` | `1420` |
| Payload mode | `mtu` | `mtu` |
| Ping count | `5` | `5` |
| Warmup | `2s` | `2s` |
| `iperf3` duration | `3s` | `3s` |
| `iperf3` bandwidth | `64K` | `64K` |
| `iperf3` datagram | `1340 B` | `1340 B` |

## Latest Artifact

| Field | PNet.Mesh | wireguard-go |
|---|---|---|
| Artifact | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| Run timestamp | `2026-07-03T12:20:45Z` | `2026-07-03T12:21:10Z` |
| Git commit | `4751e57156b4` | `4751e57156b4` |
| Version | `1.0.0.0` | `0.0.20230223-1ubuntu0.24.04.2` |
| MTU | `1420` | `1420` |
| Payload mode | `mtu` | `mtu` |
| Ping count | `5` | `5` |
| Warmup | `2s` | `2s` |
| `iperf3` duration | `3s` | `3s` |
| `iperf3` bandwidth | `64K` | `64K` |
| Report-only baseline | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |

## Iteration Result

| Field | Value |
|---|---|
| Starting baseline | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |
| Best kept checkpoint | `artifacts/benchmarks/tun-comparison/20260703T114429Z/comparison.json` |
| Final closeout artifact | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| Final relative movement | IPv6 ping -14.5%, RSS -0.4%, IPv4 ping +8.7%, CPU +21.9%; capped throughput and packet loss stayed stable. |
| Stop reason | Further low-risk TUN hot-path attempts regressed latency, RSS, or CPU; remaining gains require larger parser/channel/runtime work. |

## References

- [Peer comparison log](../peer-comparison-log.md)
- [TUN benchmark workflow](../tun-workflow.md)
