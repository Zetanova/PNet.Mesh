---
issue: 076
date: 2026-07-02
source: performance/analysis
priority: medium
status: open
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

# 076 - ACK Bitmap Processing And Packet Tracker Cleanup

## Description

Replace ACK bitmap copying with span-based bit tests and verify whether packet-tracker cleanup can shrink its rented bitmap window safely.

## Parent Tracking

- Parent: #072
- Extracted scope: ACK bitmap processing and packet-tracker cleanup.
- Standalone reason: ACK parsing and packet-tracker sizing can be validated with dedicated session regressions and benchmark proof, separate from TUN and helper work.

## Research

The session ACK path still materializes bytes for bitmap inspection, and the packet buffer still uses `BitArray` plus LINQ. The packet tracker also carries a `todo smaller implementation` note, so its ring sizing needs focused verification rather than an umbrella optimization pass.

## Scope

- In scope: `ToByteArray()` removal in ACK processing, `BitArray`/LINQ replacement, packet-tracker window math review, retained duplicate-copy behavior if benchmark evidence is insufficient.
- Out of scope: TUN packet ownership, span/IP helper work, wire-format changes.

## Acceptance Criteria

- ACK processing no longer materializes arrays solely for bitmap inspection.
- Packet-tracker sizing either stays unchanged with documented rationale or shrinks with passing tests.
- Session regression coverage proves ACK and retransmit behavior remains correct.
- Benchmarks capture any allocation difference in the ACK/session paths.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | `PNetMeshSession.ProcessAck` still calls `ToByteArray()`. | verified | source | The ACK path materializes a byte array before bitmap handling. |
| 2 | F | `PNetMeshPacketBuffer` still uses `BitArray` and LINQ for bitmap processing. | verified | source | The packet buffer helper allocates a `BitArray` and uses LINQ enumeration. |
| 3 | F | `PNetMeshPacketTracker` has a `todo smaller implementation` note and requires semantic verification before changing. | verified | source | `PNetMeshPacketTracker.cs` still includes the `todo smaller implementation` comment. |
