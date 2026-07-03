---
issue: 093
date: 2026-07-03
source: benchmark-analysis
priority: medium
status: completed
terminal-state: completed
research-status: complete
research-date: 2026-07-03
assumptions-date: 2026-07-03
completed-date: 2026-07-03
completed: 2026-07-03
completed-commits:
  - ad9edae3f5f7b3c0f4643ab791e0c459c29f52d8
brief: "description+playbook+scope+acceptance-criteria+assumptions+baseline-evidence"
views:
  enrich: "description+playbook+scope+acceptance-criteria+assumptions+baseline-evidence"
  fix: "description+playbook+scope+acceptance-criteria+assumptions+baseline-evidence"
  complete: "description+completion-report+resolving-commits"
---

# 093 - Allocation-Free IP Packet Classification

## Description

Improve IPv4/IPv6 plaintext packet parsing and classification so hot paths can classify the first byte and validate packet lengths without allocating `IPAddress` objects. Add focused microbenchmarks and a regression gate before using the result to guide session or TUN hot-path optimization.

## Playbook

- `Boundary`: benchmark the raw decrypted cleartext boundary separately from full session/protobuf parsing.
- `Classification`: classify `PNet`, IPv4, and IPv6 from the first byte, then validate only the fields needed for safe length/header decisions.
- `Parser`: prefer `ReadOnlySpan<byte>` plus `BinaryPrimitives` over C-style struct overlays for network-endian fields and variable IPv4 header length.
- `Benchmark`: add allocation-focused microbenchmarks for packet classification/header parsing independent of session setup and protobuf parsing.
- `Regression`: capture comparable baseline runs, then make allocation or throughput regressions block the benchmark gate once variance is stable.

## Scope

- Add an allocation-free IP packet header/classification API such as a `readonly ref struct` or readonly value view that exposes version, header length, total length, payload offset, and payload length.
- Avoid constructing `IPAddress` in the hot classification/read path; keep address materialization as an explicit compatibility/lazy operation if needed.
- Add benchmark cases for:
  - first-byte classification of PNet/IPv4/IPv6 plaintext,
  - IPv4 header/total-length validation,
  - IPv6 payload-length validation,
  - current `TryReadIpFrames` behavior for comparison.
- Preserve existing `PNetMeshIpPacket.TryRead` behavior or provide a compatibility wrapper if callers still need `IPAddress` properties.
- Add or update tests for malformed IPv4 IHL/total length, malformed IPv6 payload length, short packets, and valid PNet/IP classification.

## Out of Scope

- Replacing protobuf packet format.
- Changing WireGuard transport encryption/decryption behavior.
- Optimizing `ByteString` ownership or session serialization in the same change.

## Baseline Evidence

| Benchmark | Payload | Mean | Allocated | Verification |
|---|---:|---:|---:|---|
| IPv4/IPv6 frame read | 64 | 103.45 ns | 240 B/op | `timeout 900s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*'` |
| IPv4/IPv6 frame read | 1280 | 292.88 ns | 240 B/op | same 2026-07-03 run |

Relevant source evidence:

- `PNetMeshPayloadFraming.TryRead` dispatches IP payloads through `PNetMeshIpPacket.TryRead`.
- `PNetMeshIpPacket.TryReadIPv4` constructs source and destination `IPAddress` objects during parse.
- `PNetMeshIpPacket.TryReadIPv6` constructs source and destination `IPAddress` objects during parse.
- The current microbenchmark reports a fixed `240 B/op` for `IPv4/IPv6 frame read`, consistent with address object allocation rather than payload-size-scaled copying.

## Acceptance Criteria

- A microbenchmark isolates cleartext packet classification/header validation from session setup and protobuf parsing.
- Allocation-free classification/header parsing reports `0 B/op` for valid PNet, IPv4, and IPv6 inputs.
- Existing compatibility read behavior remains available for callers that need materialized `IPAddress` values.
- Three comparable benchmark runs establish variance before a blocking threshold is enabled; after that, CI or the benchmark regression script blocks allocation regression or a documented throughput regression.
- Completion report includes before/after allocation and timing for current `TryReadIpFrames` and the new classification/header benchmark cases.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Current `IPv4/IPv6 frame read` benchmark reports 240 B/op allocation. | verified | test | The 2026-07-03 Release BenchmarkDotNet run wrote `artifacts/benchmarks/results/PNet.Mesh.Benchmarks.WireGuardTransportBenchmarks-report-github.md`. |
| 2 | F | `PNetMeshIpPacket.TryReadIPv4` constructs `IPAddress` objects while parsing. | verified | source | `src/PNet.Mesh/PNetMeshIpPacket.cs` constructs source and destination `IPAddress` values in the IPv4 parse path. |
| 3 | F | `PNetMeshIpPacket.TryReadIPv6` constructs `IPAddress` objects while parsing. | verified | source | `src/PNet.Mesh/PNetMeshIpPacket.cs` constructs source and destination `IPAddress` values in the IPv6 parse path. |
| 4 | R | First-byte classification plus span-based header validation can classify PNet/IPv4/IPv6 without materializing addresses. | verified | logical | PNet marker bits and IP version bits are in byte 0; IPv4/IPv6 length validation uses fixed offsets readable from the span. |
| 5 | C | Blocking regression thresholds require stable comparable benchmark baselines. | verified | source | Project benchmark policy keeps thresholds report-only until 3+ comparable runs establish variance. |

## Completion Report

Resolved in `ad9edae3f5f7b3c0f4643ab791e0c459c29f52d8` (`Add allocation-free packet parsing benchmarks`).

The allocation-free IP classification and header parsing path landed with compatibility materialization for callers that still need `IPAddress` values. The benchmark coverage now separates first-byte classification and header validation from full frame parsing, and verification passed the Release build, the full unit test suite, scoped whitespace formatting, `git diff --check`, and focused BenchmarkDotNet evidence for payloads 128 and 1420.

## Resolving Commits

- `ad9edae3f5f7b3c0f4643ab791e0c459c29f52d8`
