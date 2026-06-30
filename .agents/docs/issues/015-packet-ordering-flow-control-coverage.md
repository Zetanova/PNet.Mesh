---
issue: 015
date: 2026-06-30
source: coverage/readme
priority: high
status: open
research-date: 2026-06-30
research-status: partial
assumptions-date: 2026-06-30
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 015 - Cover Packet Ordering And Flow Control

## Description

Expand implementation and test coverage for the README feature claim that PNet.Mesh provides packet ordering and flow control.

## Playbook

- `Ordering`: out-of-order packets are accepted/reordered or rejected according to protocol design.
- `Acknowledgment`: ack and out-of-sequence bitmap behavior drives retransmission decisions.
- `Flow control`: outstanding packet limits and negotiated SYN fields influence send behavior.

## Scope

- Inventory existing packet tracker, buffer, protocol, and session tests.
- Add missing negative cases for duplicate, expired, lost, and reordered packet windows.
- Add integration coverage that proves delivery ordering under burst and delayed-packet conditions.

## Acceptance Criteria

- Packet ordering has deterministic unit tests and at least one integration scenario.
- Flow-control limits are asserted, not only configured.
- Retransmission and ack behavior has regression coverage.

## Research

### Current State

Existing tests cover packet tracker windows, packet buffer ack behavior, and some protocol out-of-order handling. Flow-control behavior is less directly asserted.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README lists packet ordering and flow control as a feature. | verified | source | README Features includes the claim. |
| 2 | F | Existing unit tests cover packet buffer and packet tracker behavior. | verified | source | `PNetMeshPacketBufferTests` and `PNetMeshPacketTrackerTest` exist. |
| 3 | F | Flow-control behavior needs additional source/test analysis before implementation. | unverified | source | Current issue filing did not verify full session-level flow-control coverage. |

## Completion Report

Pending.
