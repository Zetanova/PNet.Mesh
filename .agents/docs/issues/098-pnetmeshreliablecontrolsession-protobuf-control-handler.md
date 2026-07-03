---
issue: 098
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

# 098 - Move PNet Reliable/Control Behavior Behind a PNet-Only Session Handler

## Description

Move the current PNet reliable/control behavior behind a PNet-only handler so ACK, retransmit, SYN/open negotiation, relay, candidate exchange, compression metadata, and protobuf packet parsing stay isolated from raw IP frame handling.

## Parent Tracking

- Parent: #095
- Extracted scope: retain the PNet reliable/control path only for frames classified as `PNet`.
- Standalone reason: the control-plane protocol surface can migrate independently once raw frame transport and dispatch exist.

## Scope

- In scope: the PNet protobuf/control session path, reliability semantics, compatibility adapters, and any glue needed to receive only `PNet` frames from the dispatcher.
- Out of scope: raw IPv4/IPv6 frame handling, frame classification, and transport crypto.

## Acceptance Criteria

- Only `PNet` frames reach protobuf parsing.
- ACK, retransmit, SYN/open negotiation, relay, candidate exchange, and compression metadata behavior remains covered.
- Existing callers can continue through a compatibility adapter while the migration is in progress.
- Build and focused control-path tests pass after the split.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `PNetMeshSession.TryReadMessage` currently parses decrypted `PNet` frames as protobuf packets. | verified | source | `src/PNet.Mesh/PNetMeshSession.cs` calls `Protos.Packet.Parser.ParseFrom(frame.Payload)`. |
| 2 | F | `PNetMeshSession.WritePayload` and `PNetMeshSession.WritePacket` currently shape outbound PNet control traffic through protobuf. | verified | source | `src/PNet.Mesh/PNetMeshSession.cs` writes `Protos.Payload`/`Protos.Packet` before encryption. |
| 3 | F | The parent issue's playbook already identifies ACK, retransmit, SYN, relay, candidate exchange, and compression metadata as PNet-only behavior. | verified | source | `/srv/projects/pnet-mesh/.agents/docs/issues/095-split-session-secure-frame-transport.md` playbook and proposed split isolate those behaviors in the PNet control component. |

## Completion Report

Resolved in `a659d9ee2aa91e0e1a68a8b5000a102ab06abfa1` (`Split secure frame, dispatcher, and PNet control (#096-#098)`).

`PNetMeshReliableControlSession` now keeps the protobuf-backed reliable/control behavior behind a PNet-only boundary. ACK, retransmit, SYN/open negotiation, relay, candidate exchange, and compression metadata remain on the control path, while raw IP frames stay out of protobuf parsing.

## Resolving Commits

- `a659d9ee2aa91e0e1a68a8b5000a102ab06abfa1`
