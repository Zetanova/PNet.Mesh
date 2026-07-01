---
issue: 027
date: 2026-06-30
source: coverage/readme
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-07-01
commits: [c8648e6]
split-status: child
parent-issue: 015
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 027 - Flow Control Negotiation Coverage

## Description

Add tests that assert negotiated flow-control limits and SYN fields influence send behavior.

## Parent Tracking

- Parent: #015
- Extracted scope: flow-control assertions.
- Standalone reason: negotiated send limits can be verified separately from packet ordering and retransmission behavior.

## Scope

- In scope: outstanding packet limits, negotiated SYN fields, send-behavior assertions.
- Out of scope: packet ordering windows, retransmit timing, README wording, parent tracking.

## Acceptance Criteria

- Flow-control limits are asserted, not just configured.
- Negotiated SYN fields affect the observed send behavior.
- Regression coverage fails if the negotiated limits are ignored.

## Research

### Current State

Parent research notes that flow-control behavior is the less directly asserted part of the current coverage surface.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Flow-control behavior needs additional source/test analysis before implementation. | verified | source | #015 research explicitly calls out the missing flow-control surface. |
| 2 | F | Negotiated SYN fields are distinct from ordering and retransmission checks. | verified | source | #015 playbook splits flow control into its own concern. |

## Completion Report

Completed in `c8648e61836106888827315db932a6e4aa9967df`.

- Added regression coverage and implementation support proving negotiated flow-control limits, SYN fields, packet-size limits, ACK-only behavior, retransmit-window cleanup, and async send/relay result failures are honored.
- Tracker row completion is now recorded in the issue index.

## Resolving Commits

- `c8648e61836106888827315db932a6e4aa9967df` - honor negotiated mesh flow-control limits
