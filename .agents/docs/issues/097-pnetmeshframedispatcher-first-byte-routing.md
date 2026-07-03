---
issue: 097
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

# 097 - Add a Frame Dispatcher for Decrypted First-Byte Routing

## Description

Introduce a dispatcher that classifies decrypted plaintext frames by their first byte and routes `IPv4`, `IPv6`, and `PNet` frames to the appropriate downstream handlers before deeper parsing happens.

## Parent Tracking

- Parent: #095
- Extracted scope: first-byte classification and handler routing for decrypted plaintext frames.
- Standalone reason: routing can be verified independently from transport crypto and PNet reliability semantics.

## Scope

- In scope: a dispatcher API that uses `PNetMeshPayloadFraming.TryClassify` to route decrypted frames to raw IP handlers or the PNet control handler.
- Out of scope: transport encryption/decryption, protobuf packet encoding, retransmit windows, and IP header parsing beyond classification.

## Acceptance Criteria

- Decrypted plaintext frames are routed by first byte before any `Protos.Packet` parsing.
- `IPv4` and `IPv6` frames bypass PNet protobuf parsing.
- `PNet` frames are forwarded to the PNet control path.
- Dispatcher behavior is covered by focused unit tests or equivalent verification.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `PNetMeshPayloadFraming.TryClassify` can classify a plaintext frame by first byte without full frame or protobuf parsing. | verified | source | `src/PNet.Mesh/PNetMeshPayloadFraming.cs` maps the first header byte to `PNet`, `IPv4`, or `IPv6`. |
| 2 | F | Existing benchmarks already isolate decrypted cleartext classification from full protobuf session parsing. | verified | source | `src/PNet.Mesh.Benchmarks/WireGuardTransportBenchmarks.cs` includes `DecryptThenClassify*Cleartext` and `ClassifyAlreadyDecrypted*FirstByte`. |
| 3 | F | The current session path still parses decrypted `PNet` frames as protobuf packets. | verified | source | `src/PNet.Mesh/PNetMeshSession.cs` calls `Protos.Packet.Parser.ParseFrom(frame.Payload)`. |

## Completion Report

Resolved in `a659d9ee2aa91e0e1a68a8b5000a102ab06abfa1` (`Split secure frame, dispatcher, and PNet control (#096-#098)`).

The new dispatcher routes decrypted plaintext frames by first byte before any deeper PNet parsing. IPv4 and IPv6 frames stay on the raw-frame side of the split, while only `PNet` frames continue toward the control path.

## Resolving Commits

- `a659d9ee2aa91e0e1a68a8b5000a102ab06abfa1`
