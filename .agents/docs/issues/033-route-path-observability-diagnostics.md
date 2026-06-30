---
issue: 033
date: 2026-06-30
source: coverage/readme
priority: high
status: ready
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
split-status: child
parent-issue: 017
brief: "description+playbook"
views:
  enrich: "description+playbook+research+assumptions"
  fix: "description+playbook+scope+acceptance-criteria+assumptions"
  complete: "description+completion-report"
---

# 033 - Route Path Observability And Diagnostics

## Description

Expose enough diagnostics to assert the actual route path, not just the final payload, for crypto routing and discovery scenarios.

## Parent Tracking

- Parent: #017
- Extracted scope: route-path observability and diagnostics.
- Standalone reason: diagnostics surface work can proceed independently of the underlying routing behavior tests.

## Scope

- In scope: route-path diagnostics, observable hop or peer information, failure diagnostics, test assertions on the observed route.
- Out of scope: router logic changes, README wording, parent tracking.

## Acceptance Criteria

- Tests can assert the actual route path, not only a final pong.
- Route-path diagnostics are available for the routing/discovery scenarios.
- Failure output makes the chosen path visible when assertions fail.

## Research

### Current State

Parent research says the current localhost coverage still relies on comments and final message checks rather than a dedicated route-path diagnostics surface.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Route-path observability is the second concern named in #017. | verified | source | #017 split guidance names route-path observability and diagnostics separately. |
| 2 | F | The current assertions are still narrow enough that a diagnostics surface would reduce brittleness. | verified | source | #017 research notes route assertions are still narrow and comments are used for discovery cases. |

## Completion Report

Pending.
