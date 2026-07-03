---
issue: 096
date: 2026-07-03
source: architecture/performance
priority: high
status: completed
terminal-state: completed
research-status: complete
research-date: 2026-07-03
split-status: child
parent-issue: 095
assumptions-date: 2026-07-03
completed-date: 2026-07-03
completed: 2026-07-03
completed-commits:
  - a659d9ee2aa91e0e1a68a8b5000a102ab06abfa1
brief: "description+parent-tracking+scope+acceptance-criteria+assumptions"
views:
  enrich: "description+parent-tracking+scope+acceptance-criteria+assumptions"
  fix: "description+parent-tracking+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report+resolving-commits"
---

# 096 - Extract PNetMeshSecureFrameSession Raw Plaintext Transport

## Description

Extract the raw secure-frame transport boundary from `PNetMeshSession` so plaintext frames can be encrypted on write and decrypted on read without requiring protobuf packet construction or parsing.

## Parent Tracking

- Parent: #095
- Extracted scope: move plaintext frame encryption, decryption, and buffer ownership into a secure-frame transport component.
- Standalone reason: the transport boundary can be implemented and benchmarked without the PNet reliable/control parser.

## Scope

- In scope: a raw frame transport API such as `TryWriteFrame` and `TryReadFrame`, encrypted packet emission, decrypted plaintext buffers, and transport-owned buffer/endpoint metadata.
- Out of scope: protobuf serialization, ACK/retransmit/SYN logic, frame dispatch, IPv4/IPv6 parsing, and routing policy.

## Acceptance Criteria

- Plaintext frame write/read paths exist without constructing or parsing `Protos.Packet`.
- The secure-frame transport boundary can be exercised independently from PNet control handling.
- Existing compatibility callers can still reach current session behavior through an adapter during migration.
- Build and focused transport tests pass after the split.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `PNetMeshSession.WritePayload` currently wraps payloads in `Protos.Payload` and `ByteString`. | verified | source | `src/PNet.Mesh/PNetMeshSession.cs` creates `new Protos.Payload()` and assigns `item.Raw = ByteString.CopyFrom(payload)`. |
| 2 | F | `PNetMeshSession.WritePacket` currently serializes `Protos.Packet` before encryption. | verified | source | `src/PNet.Mesh/PNetMeshSession.cs` calls `packet.CalculateSize()` and `packet.WriteTo(...)` before `Transport.WriteMessage(...)`. |
| 3 | F | Existing benchmarks already isolate decrypted cleartext classification from full protobuf session parsing. | verified | source | `src/PNet.Mesh.Benchmarks/WireGuardTransportBenchmarks.cs` includes cleartext decrypt and classification benchmark cases. |

## Completion Report

Resolved in `a659d9ee2aa91e0e1a68a8b5000a102ab06abfa1` (`Split secure frame, dispatcher, and PNet control (#096-#098)`).

`PNetMeshSecureFrameSession` now owns the raw plaintext transport boundary, so plaintext frame write and read paths no longer need `Protos.Packet` construction or parsing. The focused boundary tests in the resolving commit exercise the split and keep the secure-frame path independent from PNet control handling.

## Resolving Commits

- `a659d9ee2aa91e0e1a68a8b5000a102ab06abfa1`
