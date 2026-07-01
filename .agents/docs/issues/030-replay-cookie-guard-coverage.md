---
issue: 030
date: 2026-06-30
source: coverage/readme
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-07-01
commits: [032008382cb8bbf8e8efee67b33542fb83981e85]
split-status: child
parent-issue: 016
terminal-state: completed
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 030 - Replay And Cookie Guard Coverage

## Description

Add regression tests for replay behavior, cookie handling, and packet-tracker guard paths.

## Parent Tracking

- Parent: #016
- Extracted scope: replay and cookie guard.
- Standalone reason: replay-window and cookie-handshake behavior can be validated separately from handshake authentication and README wording.

## Scope

- In scope: replay windows, cookie handshakes, packet tracker guards, replay regression assertions.
- Out of scope: handshake success path, tamper rejection, README wording, parent tracking.

## Acceptance Criteria

- Replay behavior has regression coverage.
- Cookie-handshake and packet-tracker guard behavior has regression coverage.
- The scenario fails if replayed traffic is accepted.

## Research

### Current State

Parent research already notes the current coverage includes cookie handshakes, but the replay and guard paths still need isolated regression coverage.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Cookie and replay guard behavior is named separately in #016. | verified | source | #016 playbook lists cookie and replay guard as its own coverage item. |
| 2 | F | Packet tracker behavior is already present in the source/test surface. | verified | source | #016 research references existing cookie handshake coverage and replay checks. |

## Completion Report

Completed in `032008382cb8bbf8e8efee67b33542fb83981e85`.

- Added replay regression coverage proving an accepted transport packet is rejected when replayed, produces no plaintext, and clears the output buffer on duplicate rejection.
- Added cookie `mac2` guard regressions for matching-cookie acceptance and missing/wrong-cookie rejection on handshake initiation and response packets.
- Added packet tracker guard regressions for `GetBitmap` rejecting counters before `Latest` and undersized bitmap buffers.
- Verification passed formatting, Release build, targeted regressions `6/6`, `PNetMeshProtocolTest` `15/15`, `PNetMeshPacketTrackerTest` `18/18`, and the full unit suite `77/77`.

## Resolving Commits

- `032008382cb8bbf8e8efee67b33542fb83981e85` - add replay, cookie, and packet-tracker guard regressions
