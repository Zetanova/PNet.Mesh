---
issue: 022
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

# 022 - Bootstrap Discovery E2E Coverage

## Description

Add a Testcontainers scenario where a node learns peers through a bootstrap peer before exchanging payloads.

## Parent Tracking

- Parent: #009
- Extracted scope: bootstrap discovery path.
- Standalone reason: discovery through an already-connected peer can be exercised without coupling to the other scenario families.

## Scope

- In scope: bootstrap node setup, learned-peer exchange, route assertion, failure logs.
- Out of scope: direct peer-only flow, multi-hop relay, restart recovery, negative-path cases, parent tracking.

## Acceptance Criteria

- A node discovers a peer through the bootstrap path and exchanges payloads.
- The scenario fails if discovery never completes or the learned route is incorrect.
- Failure output includes the relevant container logs.

## Research

### Current State

Parent research treats discovery through a connected peer as a separate test concern from direct, multi-hop, restart, and negative-path flows.

## Assumptions

| # | Cat | Assumption | Status | Method | Detail |
|---|-----|------------|--------|--------|--------|
| 1 | F | Bootstrap discovery is one of the scenario families named in #009. | verified | source | #009 playbook lists bootstrap discovery as a separate concern. |
| 2 | F | The shared harness and route assertions from #007 and #009 are the intended foundation. | verified | source | #007 and #009 define the reusable e2e harness and scenario matrix. |

## Completion Report

Pending.
