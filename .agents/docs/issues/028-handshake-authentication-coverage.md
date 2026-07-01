---
issue: 028
date: 2026-06-30
source: coverage/readme
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-07-01
commits: [b36bd7fd8d7f82308624bbaf428daf660aaf096b]
split-status: child
parent-issue: 016
terminal-state: completed
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 028 - Handshake And Authentication Coverage

## Description

Add protocol and integration tests that prove valid peers complete the Noise IKpsk2 handshake and derive working transports.

## Parent Tracking

- Parent: #016
- Extracted scope: handshake and authentication happy path.
- Standalone reason: the successful handshake path can be verified separately from tamper, replay, and claim-hygiene work.

## Scope

- In scope: valid peer handshake, authentication success, transport derivation, regression naming.
- Out of scope: wrong-key rejection, replay/cookie guards, README wording, parent tracking.

## Acceptance Criteria

- Valid peers complete the Noise IKpsk2 handshake.
- The resulting transport can exchange payloads.
- The handshake path has a named regression test.

## Research

### Current State

Parent research already confirmed successful handshakes and cookie handshakes exist in protocol tests.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Protocol tests currently cover successful handshakes. | verified | source | #016 research names `PNetMeshProtocolTest` handshake exchange tests. |
| 2 | F | Valid peers completing IKpsk2 is the happy-path side of the security claim. | verified | source | #016 description and playbook define handshake security as a core guarantee. |

## Completion Report

Completed in `b36bd7fd8d7f82308624bbaf428daf660aaf096b`.

- Added named regression coverage proving valid peers complete the Noise IKpsk2 handshake, derive non-null transports, and exchange payloads in both directions.
- Tracker row completion is now recorded in the issue index.

## Resolving Commits

- `b36bd7fd8d7f82308624bbaf428daf660aaf096b` - add handshake/authentication regression coverage
