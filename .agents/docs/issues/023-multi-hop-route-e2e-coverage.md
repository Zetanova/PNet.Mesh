---
issue: 023
date: 2026-06-30
source: e2e/coverage
priority: high
status: completed
research-date: 2026-06-30
research-status: complete
assumptions-date: 2026-06-30
completion-date: 2026-06-30
commits: [84bb53b]
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

Completed in `84bb53b`.

- Added a four-node segmented Testcontainers topology where edge nodes on separated Docker networks exchange payloads only through relay nodes.
- Extended the e2e harness to attach nodes to multiple named networks while preserving the default single-network behavior.
- Added route-response handling needed by the segmented relay path: long relay routes stay relay-backed until a known route exists, relay-only packets are acknowledged, and relay commands flush immediately.
- Tightened relay fan-out to routable open sessions with concrete remote endpoints and added a regression for fallback to an older open session when the current session is disposed.
- Verified Release build, unit tests (`30` total), targeted multi-hop e2e (`1` total), full e2e tests (`6` total), scoped whitespace formatting, `git diff --check`, Docker cleanup, and focused reviewer approval.

## Resolving Commits

- `84bb53b6c04243b41b70931ba3e186b59bdc55f0` - add segmented multi-hop relay coverage
