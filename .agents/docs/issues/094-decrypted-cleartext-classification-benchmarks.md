---
issue: 094
date: 2026-07-03
source: benchmark-analysis
priority: medium
status: completed
terminal-state: completed
research-status: complete
research-date: 2026-07-03
assumptions-date: 2026-07-03
related-issues:
  - 092
  - 093
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

# 094 - Decrypted Cleartext Classification Benchmarks

## Description

Refactor the microbenchmark matrix to expose the decrypted cleartext boundary before PNet frame parsing or protobuf session parsing. Add focused first-byte classification benchmarks for PNet, IPv4, and IPv6 plaintext so parser and wire-format optimization work can be guided by isolated measurements.

## Playbook

- `Boundary`: decrypt transport packets into caller-owned plaintext buffers, then stop before `PNetMeshPayloadFraming.TryRead` and protobuf parsing.
- `Classification`: benchmark first-byte classification of decrypted cleartext as PNet, IPv4, or IPv6.
- `Comparison`: keep existing full frame/session benchmarks so the isolated boundary can be compared with end-to-end behavior.
- `Dependency`: use this issue as measurement groundwork before implementing parser/API changes in #093.

## Scope

- Add benchmark inputs for encrypted packets whose decrypted plaintext begins with a PNet frame, IPv4 packet, and IPv6 packet.
- Add benchmark methods that:
  - decrypt to the cleartext buffer and return cleartext byte count,
  - classify the first byte as PNet/IPv4/IPv6 without parsing the full frame,
  - optionally classify already-decrypted buffers to isolate classification cost from transport decrypt cost.
- Keep existing `WireGuard transport write/read`, `PNet frame read`, `IPv4/IPv6 frame read`, and session benchmarks for comparison.
- Report allocation and timing separately for transport decrypt, first-byte classification, frame parsing, and session parsing.

## Out of Scope

- Changing `PNetMeshPayloadFraming` parser behavior.
- Changing `PNetMeshIpPacket` allocation behavior.
- Changing protobuf session serialization or the PNet cleartext wire format.

## Baseline Evidence

| Benchmark | Payload | Mean | Allocated | Verification |
|---|---:|---:|---:|---|
| WireGuard transport write/read | 1420 | 5,988.22 ns | 0 B/op | `timeout 900s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*'` |
| PNet frame read | 1420 | 25.68 ns | 0 B/op | same 2026-07-03 run |
| IPv4/IPv6 frame read | 1420 | 127.87 ns | 240 B/op | same 2026-07-03 run |
| Session write/read payload | 1420 | 710,019.35 ns | 32419 B/op | same 2026-07-03 run |

Relevant source evidence:

- `WireGuardTransportBenchmarks.WriteThenReadTransportPacket` decrypts into `_plaintext` and stops at byte count.
- `WireGuardTransportBenchmarks.TryReadPNetFrame` measures PNet frame parsing after frame construction.
- `WireGuardTransportBenchmarks.TryReadIpFrames` measures IP frame parsing and currently includes allocation from materialized IP addresses.
- `WireGuardTransportBenchmarks.SessionWriteReadPayloadPacket` includes session write/read behavior after protobuf packet construction and parsing.

## Acceptance Criteria

- Benchmark output has distinct cases for:
  - transport decrypt to cleartext only,
  - first-byte classification for decrypted PNet cleartext,
  - first-byte classification for decrypted IPv4 cleartext,
  - first-byte classification for decrypted IPv6 cleartext,
  - existing full PNet/IP/session parsing comparisons.
- First-byte classification benchmark cases report `0 B/op`.
- The benchmark names/categories make the boundary clear enough that future reports can distinguish transport, classification, frame parsing, and session parsing costs.
- Release benchmark command runs successfully for the new focused filter and writes BenchmarkDotNet artifacts.
- Completion report records the command, allocation numbers, and timing for payload 128 and 1420.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | The current transport benchmark can decrypt into a caller-owned plaintext buffer without parsing PNet or protobuf. | verified | source | `src/PNet.Mesh.Benchmarks/WireGuardTransportBenchmarks.cs` decrypts into `_plaintext` in `WriteThenReadTransportPacket`. |
| 2 | F | The current IP frame benchmark includes full frame parsing, not just first-byte classification. | verified | source | `TryReadIpFrames` calls `PNetMeshPayloadFraming.TryRead` for IPv4 and IPv6 packets. |
| 3 | F | `PNetMeshPayloadFraming.TryRead` classifies IP versions and PNet headers from the first byte before deeper validation. | verified | source | `src/PNet.Mesh/PNetMeshPayloadFraming.cs` reads `headerByte = payload[0]` and derives version/header classification from it. |
| 4 | R | Isolating first-byte classification from frame parsing will make parser allocation and cleartext wire-format decisions easier to reason about. | verified | logical | Assumptions 1-3 show current benchmarks combine separate transport, classification, frame validation, and session/protobuf costs. |

## Completion Report

Resolved in `ad9edae3f5f7b3c0f4643ab791e0c459c29f52d8` (`Add allocation-free packet parsing benchmarks`).

The decrypted-cleartext boundary is now explicit in the benchmark matrix, with separate measurements for transport decrypt, first-byte classification, and the existing frame/session comparison cases. That split provided the isolated measurement ground truth used by #093, and verification passed the Release build, the full unit test suite, scoped whitespace formatting, `git diff --check`, and focused BenchmarkDotNet evidence for payloads 128 and 1420.

## Resolving Commits

- `ad9edae3f5f7b3c0f4643ab791e0c459c29f52d8`
