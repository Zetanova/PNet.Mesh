---
issue: 069
date: 2026-07-02
source: benchmark/hotspot
priority: medium
status: ready
baseline: 057
research-status: complete
research-date: 2026-07-02
assumptions-date: 2026-07-02
brief: "description+scope+acceptance-criteria+assumptions+playbook+baseline-evidence"
views:
  enrich: "description+scope+acceptance-criteria+assumptions+playbook+baseline-evidence"
  fix: "description+scope+acceptance-criteria+assumptions+playbook+baseline-evidence"
  complete: "description+completion-report"
---

# 069 - UDP Loopback Macro Allocation Hotspot

## Description

Reduce allocation in the UDP loopback macro harness so it better isolates protocol cost from benchmark harness overhead.

## Playbook

- `Evidence`: compare against `.agents/docs/benchmarks/2026-07-02-baseline.md`.
- `Scope`: optimize the benchmark harness, not production socket code.
- `Measurement`: preserve encrypted localhost UDP request/reply behavior.
- `Target`: reduce allocated bytes per packet and GC counts in `--macro udp-loopback`.

## Scope

- Replace allocation-heavy `UdpClient.Receive` usage in `MacroBenchmarkRunner` with socket APIs that receive into reusable buffers if practical.
- Preserve receive timeouts and localhost endpoint validation.
- Keep JSON metric names stable.
- Document any remaining unavoidable runtime/socket allocation.

## Out of Scope

- Changing `PNetMeshServer` production socket receive loops.
- Making UDP macro results blocking in CI.

## Baseline Evidence

| Benchmark | Payload | Packets/sec | Allocated | Latency |
|---|---:|---:|---:|---:|
| Macro UDP loopback | 128 | 81550 packets/sec | 422 B/packet | p99 80.806 us |

Verification command:

```bash
timeout 180s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --macro udp-loopback --payload 128 --warmup 00:00:05 --duration 00:00:30
```

Relevant source evidence:

- `MacroBenchmarkRunner.UdpLoopbackScenario` uses `UdpClient.Receive`, which returns a new byte array for each datagram.

## Acceptance Criteria

- UDP loopback macro allocated bytes per packet decreases, or the issue closes with evidence that the receive allocation cannot be removed with supported APIs.
- UDP loopback macro still reports packets/sec, payload bytes/sec, wire bytes/sec, p50/p95/p99 latency, allocated bytes, and GC counts.
- The macro scenario still validates decrypted PNet frames and payload round trip.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Baseline UDP loopback macro allocates about 422 B/packet. | verified | source | `.agents/docs/benchmarks/2026-07-02-baseline.md` records macro allocation. |
| 2 | F | The UDP macro harness uses `UdpClient.Receive`. | verified | source | `src/PNet.Mesh.Benchmarks/MacroBenchmarkRunner.cs` receives datagrams through `UdpClient.Receive`. |
| 3 | R | Reusing receive buffers can make the macro benchmark better isolate protocol cost. | verified | logical | Per-datagram array allocation belongs to harness receive mechanics rather than protocol encryption/framing. |
