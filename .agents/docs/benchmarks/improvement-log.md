---
last-entry: 2026-07-04
last-baseline: 2026-07-03-baseline.md
last-refined: 2026-07-03
---

# Benchmark Improvement Log

Compact index of benchmark improvements, regressions, neutral performance-sensitive refactors, and baseline promotions. Detailed evidence stays in issue reports or baseline documents.

| Date | Scope | Issue | Commit | Area | Metric | Direction | Baseline | Current | Delta | Result | Evidence |
|------|-------|-------|--------|------|--------|-----------|----------|---------|-------|--------|----------|
| 2026-07-04 | raw-frame-boundary | 095 | beef720+dirty | Benchmark coverage | Last raw-frame boundary | neutral | `20260703T225900Z-baseline` | `20260704T020700Z-final` | Allocations/GC unchanged at zero; timing +3.5% avg under benchmark-only diff | neutral | `artifacts/benchmarks/raw-frame/20260704T020700Z-final/WireGuardTransportBenchmarks-report.csv` |
| 2026-07-04 | raw-frame-boundary | 095 | beef720+dirty | Micro-candidates | Last raw-frame boundary | lower | `20260703T225900Z-baseline` | `20260704T015600Z` + `20260704T020300Z` | Tracker all -0.0% with targeted regressions; padding all +0.1%, IPv6 +4.0% avg | rejected | `.agents/docs/issues/095-split-session-secure-frame-transport.md#performance-closeout-2026-07-04` |
| 2026-07-03 | project | - | 4751e57+dirty | baseline | Baseline promotion | neutral | 2026-07-02 baseline | 2026-07-03 baseline | reset | neutral | [2026-07-03-baseline.md](2026-07-03-baseline.md) |
| 2026-07-03 | tun-mtu-64k | - | 4751e57+dirty | TUN bridge | Latency/RSS | lower | `20260703T103637Z` | `20260703T114429Z` | IPv4 ping -1.8%, IPv6 ping -14.3%, RSS -0.3%; CPU +9.9% watch | candidate | `artifacts/benchmarks/tun-comparison/20260703T114429Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | - | 4751e57+dirty | TUN bridge | Closeout | mixed | `20260703T103637Z` | `20260703T122011Z` | IPv6 ping -14.5%, RSS -0.4%, IPv4 ping +8.7%, CPU +21.9%; throughput/loss stable | mixed | `artifacts/benchmarks/tun-comparison/20260703T122011Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | - | 653f6e9 | TUN bridge | Current-HEAD closeout | mixed | `20260703T103637Z` | `20260703T162643Z` | IPv4 ping -3.3%, IPv6 ping -13.7%, RSS -1.2%, CPU +9.0%; threads unchanged; throughput/loss stable | mixed | `artifacts/benchmarks/tun-comparison/20260703T162643Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | - | 4751e57+dirty | IP prefix | Exact-host fast path | lower | `20260703T103637Z` | `20260703T115706Z` | RSS -1.0%, IPv4 ping +10.4%, IPv6 ping +42.9%, CPU +28.9% | rejected | `artifacts/benchmarks/tun-comparison/20260703T115706Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | - | 4751e57+dirty | Linux TUN | Direct ValueTask wrappers | lower | `20260703T103637Z` | `20260703T120212Z` | RSS -0.6%, IPv4 ping +49.4%, IPv6 ping +20.5%, CPU +36.2% | rejected | `artifacts/benchmarks/tun-comparison/20260703T120212Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | - | 4751e57+dirty | Packet parser | Raw header route matching | lower | `20260703T103637Z` | `20260703T121132Z` | IPv4 ping -4.8%, IPv6 ping +29.4%, RSS +1.3%, CPU +35.6% | rejected | `artifacts/benchmarks/tun-comparison/20260703T121132Z/comparison.json` |
| 2026-07-03 | tun-mtu-64k | - | 4751e57+dirty | TUN bridge queue | SingleWriter send queue | lower | `20260703T103637Z` | `20260703T121624Z` | IPv6 ping -10.7%, RSS -0.8%, IPv4 ping +6.5%, CPU +32.4% | rejected | `artifacts/benchmarks/tun-comparison/20260703T121624Z/comparison.json` |
