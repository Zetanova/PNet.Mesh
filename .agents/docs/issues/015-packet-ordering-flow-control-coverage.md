---
issue: 015
date: 2026-06-30
source: coverage/readme
priority: high
status: gated
split-status: parent
terminal-state: gated
gate: "Close only after child issues 026 and 027 are completed or explicitly superseded."
gate-depends:
  - 026
  - 027
gate-reason: "Tracking parent waits for fine-grained child issues"
ungate-when: "All child issues are completed"
research-date: 2026-06-30
research-status: complete
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

This is a parent tracking issue. Implement child issues, not this parent directly.

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

## Tracking

| Child | Scope | Status | Notes |
|-------|-------|--------|-------|
| #026 | Ordering and retransmission regression coverage | open | Burst, delayed, duplicate, and expired packet windows |
| #027 | Flow-control negotiation assertions | open | Outstanding limits and negotiated SYN fields |

## Residual Scope
none

## Research

### Current State

Existing tests cover packet tracker windows, packet buffer ack behavior, and some protocol out-of-order handling. Flow-control behavior is less directly asserted.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | README lists packet ordering and flow control as a feature. | verified | source | README Features includes the claim. |
| 2 | F | Existing unit tests cover packet buffer and packet tracker behavior. | verified | source | `PNetMeshPacketBufferTests` and `PNetMeshPacketTrackerTest` exist. |
| 3 | F | Flow-control behavior needs additional source/test analysis before implementation. | verified | source | The current tests cover tracker and buffer behavior, but not session-level flow-control limits. |

## Enrichment History

- 2026-06-30: Marked ready after confirming ordering and flow-control remain distinct coverage gaps even though packet tracker and buffer tests already exist. Evidence: `PNetMeshPacketBufferTests.cs`, `PNetMeshPacketTrackerTest.cs`, `MeshProtocol.proto`.

## Completion Report

Pending.
