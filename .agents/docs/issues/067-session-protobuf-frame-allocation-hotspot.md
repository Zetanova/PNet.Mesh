---
issue: 067
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

# 067 - Session Protobuf Frame Allocation Hotspot

## Description

Reduce session payload serialization, frame creation, and parse allocation pressure measured by the session microbenchmark and in-memory macro benchmark.

## Playbook

- `Evidence`: compare against `.agents/docs/benchmarks/2026-07-02-baseline.md`.
- `Scope`: focus on `PNetMeshSession.WritePayload`, `WritePacket`, and `TryReadMessage`.
- `Protocol`: preserve protobuf packet semantics and ACK/retransmit behavior.
- `Target`: reduce session `B/op` and macro allocated bytes per packet.

## Scope

- Investigate avoiding `frame.Payload.ToArray()` before `Protos.Packet.Parser.ParseFrom`.
- Investigate reducing `ByteString.CopyFrom` and `PNetMeshPayloadFraming.CreatePNet` allocations on the hot send path.
- Preserve retransmit buffer ownership and packet lifetime safety.
- Add regression tests if ownership or parse behavior changes.

## Out of Scope

- Replacing protobuf schema or changing packet wire format.
- Rewriting channel/session architecture without benchmark evidence.

## Baseline Evidence

| Benchmark | Payload | Mean | Allocated | Verification |
|---|---:|---:|---:|---|
| Session write/read payload | 1420 | 712,276.74 ns | 43886 B/op | `timeout 900s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --filter '*SessionWriteReadPayloadPacket*'` |
| Macro in-memory | 128 | 164865 packets/sec | 2540 B/packet | `timeout 180s dotnet run --project src/PNet.Mesh.Benchmarks/PNet.Mesh.Benchmarks.csproj -c Release --no-build -- --macro in-memory --payload 128 --warmup 00:00:05 --duration 00:00:30` |

Relevant source evidence:

- `PNetMeshSession.WritePayload` copies payload spans into protobuf `ByteString`.
- `PNetMeshSession.WritePacket` creates a new PNet frame before transport encryption.
- `PNetMeshSession.TryReadMessage` parses protobuf from `frame.Payload.ToArray()`.

## Acceptance Criteria

- Benchmark evidence shows lower `Allocated` for `SessionWriteReadPayloadPacket` or lower macro in-memory allocated bytes per packet, or the issue closes with source-backed proof that protobuf ownership requires current copies.
- Existing unit tests pass.
- ACK, retransmit, payload ordering, and direct peer exchange coverage remain green.
- Completion report includes before/after allocation and latency/throughput metrics.

## Completion Report

Implemented in `d667db3`.

- Parsed PNet protobuf payloads from spans instead of allocating intermediate arrays on receive.
- Added pooled/buffer-writing send paths where ownership is clear and preserved retransmit buffer safety.
- Kept ACK, retransmission, ordering, and direct peer exchange behavior covered by the expanded routing tests.
- Benchmark evidence improved allocation in measured paths:
  - `SessionWriteReadPayloadPacket` payload 1420: baseline 43886 B/op; post-change 39.09 KB/op.
  - Macro in-memory payload 128: baseline about 2540 B/packet; post-change `10795308320` allocated bytes over `5304880` packets, about 2035 B/packet.
- Verification: Release build, 175/175 unit tests, 10/10 TUN unit tests, all five bounded Testcontainers e2e batches, and the final TUN benchmark smoke passed on 2026-07-02.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|---|---|---|---|---|
| 1 | F | Baseline session write/read allocates up to 43886 B/op. | verified | source | `.agents/docs/benchmarks/2026-07-02-baseline.md` records session allocation. |
| 2 | F | Baseline in-memory macro allocates about 2540 B/packet. | verified | source | `.agents/docs/benchmarks/2026-07-02-baseline.md` records macro allocation. |
| 3 | F | The current read path calls `ParseFrom(frame.Payload.ToArray())`. | verified | source | `src/PNet.Mesh/PNetMeshSession.cs` parses packet payloads from a new array. |
| 4 | R | Reducing protobuf/frame copies can reduce session allocation. | verified | logical | The measured paths create, encrypt, decrypt, frame, and parse protobuf payloads on each operation. |
