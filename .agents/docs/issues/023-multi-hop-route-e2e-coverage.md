---
issue: 023
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

# 023 - Multi-Hop Route E2E Coverage

## Description

Add a Testcontainers scenario that proves peers on separated segments communicate through a relay path with at least one intermediate hop.

## Parent Tracking

- Parent: #009
- Extracted scope: multi-hop relay route.
- Standalone reason: route traversal across segments is independently testable from bootstrap, restart, and negative-path cases.

## Scope

- In scope: separated segments, relay path selection, hop assertions, failure logs.
- Out of scope: direct peer-only flow, bootstrap discovery, restart recovery, negative-path cases, parent tracking.

## Acceptance Criteria

- Payload delivery crosses at least one intermediate hop.
- The scenario fails if hop selection or route traversal is incorrect.
- Failure output includes the relevant container logs.

## Research

### Current State

Parent research identified multi-hop relay delivery as a distinct scenario family and the current route assertions are still narrow.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Multi-hop relay delivery is one of the scenario families named in #009. | verified | source | #009 playbook lists multi-hop route coverage as a separate concern. |
| 2 | F | The shared topology builder from #007 can model separated segments and relay hops. | verified | source | #007 scope defines reusable node topology helpers. |

## Completion Report

Pending.
