---
issue: 066
date: 2026-07-02
source: benchmark/hotspot
priority: medium
status: completed
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - d667db3
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

# 066 - WireGuard Hash/MAC Allocation Hotspot

## Description

Reduce allocation pressure in WireGuard hash and MAC paths measured by the baseline handshake and rejection benchmarks. The suspected source is span-to-array conversion in BLAKE2s hash/MAC helpers.

## Playbook

- `Evidence`: compare against `.agents/docs/benchmarks/2026-07-02-baseline.md`.
- `Scope`: focus on `PNetMeshProtocol.ComputeHash`, `PNetMeshProtocol.ComputeMac`, and directly related call sites.
- `Safety`: preserve WireGuard MAC/hash behavior with existing security and packet parser tests.
- `Target`: reduce `B/op` without weakening validation or changing packet bytes.

## Scope

- Inspect BouncyCastle BLAKE2s APIs for span-friendly or buffer-reuse options.
- Remove avoidable `payload.ToArray()` and `key.ToArray()` allocations in hash/MAC helpers when safe.
- Keep cryptographic behavior byte-for-byte compatible.
- Add or update focused tests only if implementation changes observable helper behavior.

## Out of Scope

- Changing WireGuard packet formats or MAC validation semantics.
- Optimizing unrelated relay registry copies without benchmark evidence.

## Baseline Evidence

| Benchmark | Payload | Mean | Allocated | Verification |
|---|---:|---:|---:|---|
| WireGuard full handshake setup | 64 | 632,213.10 ns | 21394 B/op | `timeout 900s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*FullHandshakeSetup*'` |
| WireGuard rejection paths | 1420 | 1,939,149.80 ns | 68955 B/op | `timeout 900s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*RejectInvalidTransportPackets*'` |

Relevant source evidence:

- `PNetMeshProtocol.ComputeHash` converts `ReadOnlySpan<byte>` payloads to arrays before `Blake2sDigest.BlockUpdate`.
- `PNetMeshProtocol.ComputeMac` converts key and payload spans to arrays before constructing/updating `Blake2sDigest`.

## Acceptance Criteria

- Benchmark evidence shows lower `Allocated` for `FullHandshakeSetup` or `RejectInvalidTransportPackets`, or the issue closes with source-backed proof that BouncyCastle requires these arrays.
- Existing unit tests pass.
- WireGuard packet/MAC behavior remains byte-for-byte compatible.
- Completion report includes before/after `Allocated`, `Mean`, and verification commands.

## Completion Report

Implemented in `d667db3`.

- Removed avoidable BLAKE2s helper array conversions from WireGuard hash/MAC paths while preserving packet/MAC behavior.
- Added allocation regression coverage for the WireGuard BLAKE2s helper path.
- Benchmark evidence improved allocation in the measured paths:
  - `FullHandshakeSetup` payload 64: baseline 21394 B/op; post-change 19.98 KB/op.
  - `FullHandshakeSetup` payload 1420: post-change 20.01 KB/op.
  - `RejectInvalidTransportPackets` payload 1420: baseline 68955 B/op; post-change 64.69 KB/op.
  - `RejectInvalidTransportPackets` payload 64: post-change 60.7 KB/op.
- Verification: Release build, 175/175 unit tests, 10/10 TUN unit tests, all five bounded Testcontainers e2e batches, and the final TUN benchmark smoke passed on 2026-07-02.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Baseline handshake setup allocates about 21 KB/op. | verified | source | `.agents/docs/benchmarks/2026-07-02-baseline.md` records `WireGuard full handshake setup` allocation. |
| 2 | F | Baseline rejection paths allocate up to 68955 B/op. | verified | source | `.agents/docs/benchmarks/2026-07-02-baseline.md` records `WireGuard rejection paths` allocation. |
| 3 | F | `PNetMeshProtocol` currently converts hash/MAC span inputs to arrays. | verified | source | `src/PNet.Mesh/PNetMeshProtocol.cs` contains `payload.ToArray()` and `key.ToArray()` in hash/MAC helpers. |
| 4 | R | Reducing helper array conversion can reduce handshake/rejection allocation. | verified | logical | The measured benchmark paths call these helpers repeatedly and the source shows avoidable managed array creation candidates. |
