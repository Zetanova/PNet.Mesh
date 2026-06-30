---
issue: 025
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

# 025 - Negative Path E2E Coverage

## Description

Add a Testcontainers scenario that proves invalid keys, invalid PSKs, or corrupt routes do not deliver accepted payloads.

## Parent Tracking

- Parent: #009
- Extracted scope: negative-path delivery failure.
- Standalone reason: failure behavior is independently testable from the positive routing and restart scenarios.

## Scope

- In scope: invalid keys, invalid PSKs, corrupt routes, non-delivery assertions, failure logs.
- Out of scope: direct peer-only flow, bootstrap discovery, multi-hop relay, restart recovery, parent tracking.

## Acceptance Criteria

- Invalid inputs do not produce accepted payload delivery.
- The scenario fails if an invalid route or identity is incorrectly accepted.
- Failure output includes the relevant container logs.

## Research

### Current State

Parent research treats negative-path delivery failure as a separate concern from the direct and routed success paths.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Negative-path coverage is one of the scenario families named in #009. | verified | source | #009 playbook lists negative paths as a separate concern. |
| 2 | F | The shared harness can assert non-delivery through logs or test events. | verified | source | #007 and #009 define the e2e harness and failure-diagnostics requirements. |

## Completion Report

Pending.
