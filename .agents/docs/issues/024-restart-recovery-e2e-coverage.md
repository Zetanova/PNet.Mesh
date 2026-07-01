---
issue: 024
date: 2026-06-30
source: e2e/coverage
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-06-30
commits: [6a02e9d]
split-status: child
parent-issue: 009
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 024 - Restart Recovery E2E Coverage

## Description

Add a Testcontainers scenario where one non-bootstrap node restarts and rejoins without breaking unrelated peers.

## Parent Tracking

- Parent: #009
- Extracted scope: restart recovery.
- Standalone reason: restart and rejoin behavior can be validated without coupling to the other scenario families.

## Scope

- In scope: one-node restart, rejoin, continued traffic for unrelated peers, failure logs.
- Out of scope: direct peer-only flow, bootstrap discovery, multi-hop relay, negative-path cases, parent tracking.

## Acceptance Criteria

- A restarted node rejoins and resumes communication.
- Unrelated peers continue exchanging payloads during the restart.
- Failure output includes the relevant container logs.

## Research

### Current State

Parent research identified restart recovery as a separate scenario family rather than a variation of direct routing.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Restart recovery is one of the scenario families named in #009. | verified | source | #009 playbook lists restart recovery as a separate concern. |
| 2 | F | The e2e harness can restart individual containers without rebuilding the entire topology. | verified | source | #007 defines reusable container lifecycle helpers. |

## Completion Report

Completed in `6a02e9d`.

- Added timestamp-scoped log helpers in the Testcontainers harness so restart and rejoin checks can anchor on `stoppedAt` and `restartedAt`.
- Added a restart recovery topology and regression test that proves `node01` joins, is stopped, unrelated `node10` and `node11` start and exchange traffic after `stoppedAt`, and the same `node01` container restarts and rejoins after `restartedAt`.
- Verified the supplied build, unit, targeted restart E2E, targeted multi-hop E2E, full E2E, scoped whitespace format, and `git diff --check` evidence, plus Docker cleanup with no matching `pnet-mesh` containers.
- Tracker row completion is now recorded in the issue index.

## Resolving Commits

- `6a02e9de5d17f18914341a2ddf5fbfb164464fcf` - add restart recovery Testcontainers coverage
