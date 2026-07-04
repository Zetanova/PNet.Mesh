---
last-entry: 2026-07-04
last-refined: 2026-07-03
last-artifact: artifacts/benchmarks/tun-comparison/noise-removal-candidate-mtu-3/comparison.json
---

# Peer Comparison Log

Compact index of project-vs-peer benchmark results. Detailed setup and caveats stay in comparison definitions.

| Date | Scenario | Metric | Direction | Project | Peer | Ratio/Delta | Result | Evidence |
|------|----------|--------|-----------|---------|------|-------------|--------|----------|
| 2026-07-04 | tun-mtu-64k | IPv4 ping avg latency | lower | 2.052-2.400 ms | 1.037-2.107 ms | mixed, 0.97x-2.12x peer | mixed | `.agents/plans/remove-noise-net-direct-libsodium-wireguard.md#completion-report-2026-07-04` |
| 2026-07-04 | tun-mtu-64k | IPv6 ping avg latency | lower | 1.944-3.361 ms | 0.867-1.229 ms | 1.89x-3.43x higher | worse | `.agents/plans/remove-noise-net-direct-libsodium-wireguard.md#completion-report-2026-07-04` |
| 2026-07-04 | tun-mtu-64k | IPv4/IPv6 packet loss | lower | 0% | 0% | tied | neutral | `.agents/plans/remove-noise-net-direct-libsodium-wireguard.md#completion-report-2026-07-04` |
| 2026-07-04 | tun-mtu-64k | IPv4 iperf throughput | higher | 64.259-64.284 Kbit/s | 64.281-64.296 Kbit/s | capped parity | neutral | `.agents/plans/remove-noise-net-direct-libsodium-wireguard.md#completion-report-2026-07-04` |
| 2026-07-04 | tun-mtu-64k | IPv6 iperf throughput | higher | 64.031-64.287 Kbit/s | 64.269-64.289 Kbit/s | capped parity | neutral | `.agents/plans/remove-noise-net-direct-libsodium-wireguard.md#completion-report-2026-07-04` |
| 2026-07-04 | tun-mtu-64k | RSS | lower | 139.9-141.6 MB | 12.9-13.5 MB | 10.48x-10.81x higher | worse | `.agents/plans/remove-noise-net-direct-libsodium-wireguard.md#completion-report-2026-07-04` |
| 2026-07-04 | tun-mtu-64k | Threads | lower | 38-39 | 26 | 1.46x-1.50x higher | worse | `.agents/plans/remove-noise-net-direct-libsodium-wireguard.md#completion-report-2026-07-04` |
| 2026-07-04 | tun-mtu-64k | CPU ticks | lower | 342-368 | 14-16 | 23.00x-24.43x higher | worse | `.agents/plans/remove-noise-net-direct-libsodium-wireguard.md#completion-report-2026-07-04` |
| 2026-07-02 | tun-mtu-64k | IPv4 ping avg latency | lower | 1.876 ms | 1.020 ms | 1.84x higher | worse | `artifacts/benchmarks/tun-comparison/20260702T161457Z/comparison.json` |
| 2026-07-02 | tun-mtu-64k | IPv6 ping avg latency | lower | 2.079 ms | 0.982 ms | 2.12x higher | worse | `artifacts/benchmarks/tun-comparison/20260702T161457Z/comparison.json` |
| 2026-07-02 | tun-mtu-64k | IPv4 packet loss | lower | 0% | 0% | tied | neutral | `artifacts/benchmarks/tun-comparison/20260702T161457Z/comparison.json` |
| 2026-07-02 | tun-mtu-64k | IPv6 packet loss | lower | 0% | 0% | tied | neutral | `artifacts/benchmarks/tun-comparison/20260702T161457Z/comparison.json` |
| 2026-07-02 | tun-mtu-64k | IPv4 iperf throughput | higher | 64.283 Kbit/s | 64.291 Kbit/s | -0.012%, capped | neutral | `artifacts/benchmarks/tun-comparison/20260702T161457Z/comparison.json` |
| 2026-07-02 | tun-mtu-64k | IPv6 iperf throughput | higher | 64.279 Kbit/s | 64.292 Kbit/s | -0.020%, capped | neutral | `artifacts/benchmarks/tun-comparison/20260702T161457Z/comparison.json` |
| 2026-07-02 | tun-mtu-64k | RSS | lower | 138.7 MB | 13.1 MB | 10.62x higher | worse | `artifacts/benchmarks/tun-comparison/20260702T161457Z/comparison.json` |
| 2026-07-02 | tun-mtu-64k | Threads | lower | 37 | 26 | 1.42x higher | worse | `artifacts/benchmarks/tun-comparison/20260702T161457Z/comparison.json` |
| 2026-07-02 | tun-mtu-64k | CPU ticks | lower | 362 | 14 | 25.86x higher | worse | `artifacts/benchmarks/tun-comparison/20260702T161457Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv4 ping avg latency | lower | 2.018 ms | 1.017 ms | 1.98x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv6 ping avg latency | lower | 2.315 ms | 1.236 ms | 1.87x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv4 packet loss | lower | 0% | 0% | tied | neutral | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv6 packet loss | lower | 0% | 0% | tied | neutral | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv4 iperf throughput | higher | 64.268 Kbit/s | 64.285 Kbit/s | -0.027%, capped | neutral | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv6 iperf throughput | higher | 64.281 Kbit/s | 64.291 Kbit/s | -0.016%, capped | neutral | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | RSS | lower | 138.8 MB | 12.9 MB | 10.77x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | Threads | lower | 38 | 27 | 1.41x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | CPU ticks | lower | 343 | 14 | 24.50x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T103637Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv4 ping avg latency | lower | 2.194 ms | 1.369 ms | 1.60x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv6 ping avg latency | lower | 1.980 ms | 0.886 ms | 2.23x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv4 packet loss | lower | 0% | 0% | tied | neutral | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv6 packet loss | lower | 0% | 0% | tied | neutral | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv4 iperf throughput | higher | 64.275 Kbit/s | 64.294 Kbit/s | -0.028%, capped | neutral | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv6 iperf throughput | higher | 64.275 Kbit/s | 64.286 Kbit/s | -0.017%, capped | neutral | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | RSS | lower | 138.4 MB | 13.1 MB | 10.53x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | Threads | lower | 38 | 26 | 1.46x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | CPU ticks | lower | 418 | 15 | 27.87x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv4 ping avg latency | lower | 1.951 ms | 1.398 ms | 1.40x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T162643Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv6 ping avg latency | lower | 1.998 ms | 1.362 ms | 1.47x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T162643Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv4 packet loss | lower | 0% | 0% | tied | neutral | `artifacts/benchmarks/tun-comparison/20260703T162643Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv6 packet loss | lower | 0% | 0% | tied | neutral | `artifacts/benchmarks/tun-comparison/20260703T162643Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv4 iperf throughput | higher | 64.265 Kbit/s | 64.269 Kbit/s | -0.007%, capped | neutral | `artifacts/benchmarks/tun-comparison/20260703T162643Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | IPv6 iperf throughput | higher | 64.248 Kbit/s | 64.200 Kbit/s | +0.075%, capped | neutral | `artifacts/benchmarks/tun-comparison/20260703T162643Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | RSS | lower | 137.1 MB | 13.2 MB | 10.40x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T162643Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | Threads | lower | 38 | 28 | 1.36x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T162643Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | CPU ticks | lower | 374 | 16 | 23.38x higher | worse | `artifacts/benchmarks/tun-comparison/20260703T162643Z/comparison.json` |
