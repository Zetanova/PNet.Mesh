---
issue: 074
date: 2026-07-02
source: performance/analysis
priority: medium
status: completed
terminal-state: completed
completed-date: 2026-07-02
completed-commits:
  - 0433a9e
research-date: 2026-07-02
research-status: complete
assumptions-date: 2026-07-02
split-status: child
parent-issue: 072
brief: "description+parent-tracking+research+scope+acceptance-criteria+assumptions"
views:
  enrich: "description+parent-tracking+research+scope+acceptance-criteria+assumptions"
  fix: "description+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 074 - Span-Based Byte-Key And IP-Byte Helper Optimizations

## Description

Optimize the byte-key lookup and IP-byte helper hot paths extracted from parent #072 by replacing array-cloning lookups and temporary `GetAddressBytes()` allocations where spans and stack-backed writes are safe.

## Parent Tracking

- Parent: #072
- Extracted scope: span-based byte-key lookup plus IP-byte helper allocations.
- Standalone reason: lookup helpers and IP byte conversion can be implemented and benchmarked independently of TUN ownership and ACK cleanup.

## Research

The parent issue already identified the current byte-key comparer and span lookup hotspot, plus the `GetAddressBytes()` allocation sites in the IP helper paths. Those source references make this slice self-contained enough to track separately from TUN and ACK work.

## Scope

- In scope: `PNetMeshByteArrayComparer` span-aware lookup support, peer/lease/route dictionary alternate lookups, `IPAddress.TryWriteBytes` replacements in prefix and packet helpers, benchmark coverage for the helper paths.
- Out of scope: TUN owner transfer, ACK bitmap cleanup, packet-tracker ring resizing, protobuf wire-format changes.

## Acceptance Criteria

- Peer, lease, and route lookups avoid `ToArray()` allocations when span inputs are available.
- Hot IP helper paths use stack-backed or span-backed byte writes where safe.
- Tests cover lookup correctness and IP helper behavior, and benchmarks capture the allocation impact.

## Completion Report

Implemented in `0433a9e`.

- Added span-aware byte-array equality/hash support with alternate dictionary lookups for peer, relay lease, route, and handshake replay paths.
- Replaced small IP helper `GetAddressBytes()` allocation paths with `TryWriteBytes` into packet spans or stack buffers where safe.
- Added focused regression coverage for span lookup correctness and IP helper behavior across core and TUN tests.
- Benchmark evidence against `.agents/docs/benchmarks/2026-07-02-baseline.md` showed lower allocations in the measured hot paths:
  - `FullHandshakeSetup` decreased from about 21.4 KB/op to about 19.9 KB/op.
  - `RejectInvalidTransportPackets` decreased from about 64.9-69.0 KB/op to about 60.4-64.5 KB/op.
  - Session write/read paths decreased from about 36.5-43.9 KB/op to about 34.8-39.6 KB/op.
  - UDP-loopback macro allocation per packet decreased from about 422 bytes to about 5.3 bytes.

Verification on 2026-07-02:

- `timeout 180s rtk dotnet format whitespace PNet.Mesh.sln --include <#074 files> --no-restore --verify-no-changes --verbosity minimal` passed.
- `timeout 240s rtk dotnet build PNet.Mesh.sln -c Release --no-restore` passed with 0 errors; nullable warnings remain from the existing dirty nullable-enabled tree.
- Focused core unit tests for peer state, relay registry, protocol, routing, and IP packet helpers passed, 117/117 tests.
- Focused TUN unit tests for `PNetMeshTunBridgeTests` passed, 3/3 tests.
- Full BenchmarkDotNet microbenchmarks and the `--macro all --payload 128` macro harness passed.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Before #074 implementation, the peer lookup path cloned remote public keys with `ToArray()` before dictionary access. | verified | source | `PNetMeshWireGuardPeerState.cs` called `remotePublicKey.ToArray()` before the lookup. |
| 2 | F | Before #074 implementation, `PNetMeshByteArrayComparer` relied on `StructuralComparisons`. | verified | source | `PNetMeshHelpers.cs` defined the comparer through `StructuralComparisons.StructuralEqualityComparer`. |
| 3 | F | Before #074 implementation, small IP helper paths allocated with `GetAddressBytes()` in prefix, packet, and utility helpers. | verified | source | `IpPrefix.cs`, `PNetMeshIpPacket.cs`, and `PNetMeshUtils.cs` called `GetAddressBytes()`. |
