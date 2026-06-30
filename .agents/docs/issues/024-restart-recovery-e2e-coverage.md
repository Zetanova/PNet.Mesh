---
issue: 024
date: 2026-06-30
source: e2e/coverage
priority: high
status: ready
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
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

Pending.
